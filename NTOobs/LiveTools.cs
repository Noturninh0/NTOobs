using System.Diagnostics;
using System.IO;

namespace NTOobs;

/// <summary>Marcar momento: salva o horário da live/gravação num arquivo de texto, para achar os cortes depois.</summary>
public static class Markers
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NTOobs", "Marcadores");

    public static string TodayFile => Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd") + ".txt");

    public static async Task<string> AddAsync(ObsClient obs, string note)
    {
        string live = "", rec = "";
        try
        {
            var s = await obs.RequestAsync("GetStreamStatus");
            if (ObsTools.GetBool(s, "outputActive")) live = ObsTools.ShortTimecode(ObsTools.GetString(s, "outputTimecode"));
        }
        catch { }
        try
        {
            var r = await obs.RequestAsync("GetRecordStatus");
            if (ObsTools.GetBool(r, "outputActive")) rec = ObsTools.ShortTimecode(ObsTools.GetString(r, "outputTimecode"));
        }
        catch { }

        var parts = new List<string> { DateTime.Now.ToString("HH:mm:ss") };
        if (live.Length > 0) parts.Add("live " + live);
        if (rec.Length > 0) parts.Add("gravação " + rec);
        if (live.Length == 0 && rec.Length == 0) parts.Add("(fora do ar)");

        var line = string.Join("   ", parts);
        if (!string.IsNullOrWhiteSpace(note)) line += "   — " + note.Trim();

        Directory.CreateDirectory(Folder);
        await File.AppendAllTextAsync(TodayFile, line + Environment.NewLine);
        return line;
    }

    /// <summary>Últimas linhas do arquivo de hoje.</summary>
    public static string[] Recent(int count)
    {
        try
        {
            if (!File.Exists(TodayFile)) return Array.Empty<string>();
            var lines = File.ReadAllLines(TodayFile);
            return lines.Skip(Math.Max(0, lines.Length - count)).Reverse().ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>Soundboard: toca arquivos de áudio por uma fonte de mídia do OBS (o som vai para a live).</summary>
public sealed class Soundboard
{
    public const string InputName = "NTOobs Soundboard";
    bool _ready;

    public static string Folder => Path.Combine(ConfigStore.Dir, "Sons");

    public void Reset() => _ready = false;

    /// <summary>Copia o arquivo para a pasta do NTOobs, para o som não sumir se o original mudar de lugar.</summary>
    public static string Import(string file, string? folder = null)
    {
        folder ??= Folder;
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(file);
        var dest = Path.Combine(folder, name);
        int i = 2;
        while (File.Exists(dest) && !SameFile(file, dest))
            dest = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i++}){Path.GetExtension(name)}");
        if (!SameFile(file, dest)) File.Copy(file, dest, true);
        return dest;
    }

    static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public async Task PlayAsync(ObsClient obs, SoundItem sound, bool monitor, Action<string> log)
    {
        if (!File.Exists(sound.File))
            throw new InvalidOperationException($"O arquivo do som \"{sound.Name}\" não foi encontrado.");

        try
        {
            await PlayCoreAsync(obs, sound, monitor, log);
        }
        catch (InvalidOperationException) when (_ready)
        {
            // a fonte pode ter sido apagada no OBS: recria e tenta de novo
            _ready = false;
            await PlayCoreAsync(obs, sound, monitor, log);
        }
    }

    async Task PlayCoreAsync(ObsClient obs, SoundItem sound, bool monitor, Action<string> log)
    {
        var settings = new
        {
            local_file = sound.File,
            is_local_file = true,
            looping = false,
            restart_on_activate = false,
            close_when_inactive = false
        };

        if (!_ready)
        {
            var (scenes, current) = await MusicObs.GetScenesAsync(obs);
            if (!await MusicObs.InputExistsAsync(obs, InputName))
            {
                await obs.RequestAsync("CreateInput", new
                {
                    sceneName = current,
                    inputName = InputName,
                    inputKind = "ffmpeg_source",
                    inputSettings = settings,
                    sceneItemEnabled = true
                });
                log("Fonte do soundboard criada no OBS.");
            }
            await MusicObs.EnsureInAllScenesAsync(obs, InputName, scenes, false, log);
            await MusicObs.UnmuteAsync(obs, InputName);
            _ready = true;
        }

        try
        {
            await obs.RequestAsync("SetInputAudioMonitorType", new
            {
                inputName = InputName,
                monitorType = monitor ? "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT" : "OBS_MONITORING_TYPE_NONE"
            });
        }
        catch { }

        await obs.RequestAsync("SetInputSettings", new { inputName = InputName, inputSettings = settings, overlay = true });
        await obs.RequestAsync("SetInputVolume", new { inputName = InputName, inputVolumeMul = Math.Clamp(sound.Volume, 0, 100) / 100.0 });
        await obs.RequestAsync("TriggerMediaInputAction", new
        {
            inputName = InputName,
            mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"
        });
    }

    public static async Task StopAsync(ObsClient obs)
    {
        try
        {
            await obs.RequestAsync("TriggerMediaInputAction", new
            {
                inputName = InputName,
                mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP"
            });
        }
        catch { }
    }
}

/// <summary>Replay instantâneo: salva o Replay Buffer, mostra o vídeo numa cena e volta sozinho.</summary>
public sealed class InstantReplay
{
    public const string VideoInput = "NTOobs Replay Vídeo";
    bool _running;

    public async Task RunAsync(ObsClient obs, string scene, int seconds, Action<string> log)
    {
        if (_running) return;
        _running = true;
        try
        {
            var rb = await obs.RequestAsync("GetReplayBufferStatus");
            if (!ObsTools.GetBool(rb, "outputActive"))
            {
                try
                {
                    await obs.RequestAsync("StartReplayBuffer");
                    log("O Replay Buffer estava desligado. Liguei agora: aperte de novo daqui a alguns segundos.");
                }
                catch (Exception ex)
                {
                    log("Não consegui ligar o Replay Buffer. No OBS: Configurações, Saída, Replay Buffer, marque Ativar. (" + ex.Message + ")");
                }
                return;
            }

            var before = await LastReplayAsync(obs);
            await obs.RequestAsync("SaveReplayBuffer");

            var path = "";
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(200);
                path = await LastReplayAsync(obs);
                if (path.Length > 0 && path != before) break;
            }
            if (path.Length == 0 || path == before)
            {
                log("O OBS não informou o arquivo do replay salvo.");
                return;
            }

            if (string.IsNullOrWhiteSpace(scene)) scene = "NTOobs Replay";
            var back = await ObsTools.GetCurrentSceneAsync(obs);
            await EnsureSceneAsync(obs, scene, path, log);

            await obs.RequestAsync("SetCurrentProgramScene", new { sceneName = scene });
            await obs.RequestAsync("TriggerMediaInputAction", new
            {
                inputName = VideoInput,
                mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"
            });
            log("Replay no ar: " + Path.GetFileName(path));

            var sw = Stopwatch.StartNew();
            int ms = seconds > 0 ? seconds * 1000 : 0;
            if (ms == 0)
            {
                for (int i = 0; i < 15 && ms == 0; i++)
                {
                    await Task.Delay(200);
                    try
                    {
                        var st = await obs.RequestAsync("GetMediaInputStatus", new { inputName = VideoInput });
                        ms = (int)ObsTools.GetDouble(st, "mediaDuration");
                    }
                    catch { }
                }
            }
            if (ms <= 0) ms = 15000;
            ms = Math.Min(ms, 120000);

            var rest = ms - (int)sw.ElapsedMilliseconds;
            if (rest > 0) await Task.Delay(rest);

            // só volta se ninguém trocou de cena durante o replay
            var now = await ObsTools.GetCurrentSceneAsync(obs);
            if (now == scene && back != scene)
                await obs.RequestAsync("SetCurrentProgramScene", new { sceneName = back });
        }
        finally
        {
            _running = false;
        }
    }

    static async Task<string> LastReplayAsync(ObsClient obs)
    {
        try
        {
            var d = await obs.RequestAsync("GetLastReplayBufferReplay");
            return ObsTools.GetString(d, "savedReplayPath");
        }
        catch
        {
            return "";
        }
    }

    static async Task EnsureSceneAsync(ObsClient obs, string scene, string path, Action<string> log)
    {
        if (!await ObsTools.SceneExistsAsync(obs, scene))
        {
            await obs.RequestAsync("CreateScene", new { sceneName = scene });
            log($"Cena \"{scene}\" criada para o replay.");
        }

        var settings = new
        {
            local_file = path,
            is_local_file = true,
            looping = false,
            restart_on_activate = true,
            close_when_inactive = true
        };

        if (!await MusicObs.InputExistsAsync(obs, VideoInput))
        {
            var created = await obs.RequestAsync("CreateInput", new
            {
                sceneName = scene,
                inputName = VideoInput,
                inputKind = "ffmpeg_source",
                inputSettings = settings,
                sceneItemEnabled = true
            });
            await ObsTools.FullscreenAsync(obs, scene, created.GetProperty("sceneItemId").GetInt32());
            return;
        }

        await obs.RequestAsync("SetInputSettings", new { inputName = VideoInput, inputSettings = settings, overlay = true });
        if (!await ObsTools.HasItemAsync(obs, scene, VideoInput))
        {
            var item = await obs.RequestAsync("CreateSceneItem", new { sceneName = scene, sourceName = VideoInput, sceneItemEnabled = true });
            await ObsTools.FullscreenAsync(obs, scene, item.GetProperty("sceneItemId").GetInt32());
        }
    }
}

public sealed record HealthSnapshot(
    bool Live, bool Reconnecting, string Time, double Kbps, double DropPct, double Cpu, double Fps,
    double RenderLagPct, double EncodeLagPct, bool Recording, string RecordTime);

/// <summary>Saúde da live: bitrate, quadros perdidos, CPU e FPS, com aviso quando algo vai mal.</summary>
public sealed class LiveHealth
{
    long _bytes, _netSkip, _netTotal, _rSkip, _rTotal, _oSkip, _oTotal;
    DateTime _at = DateTime.MinValue;
    DateTime _lastAlert = DateTime.MinValue;

    public async Task<(HealthSnapshot Snap, string? Alert)> PollAsync(ObsClient obs)
    {
        var stats = await obs.RequestAsync("GetStats");
        var stream = await obs.RequestAsync("GetStreamStatus");
        bool recording = false;
        string recTime = "";
        try
        {
            var rec = await obs.RequestAsync("GetRecordStatus");
            recording = ObsTools.GetBool(rec, "outputActive");
            recTime = ObsTools.ShortTimecode(ObsTools.GetString(rec, "outputTimecode"));
        }
        catch { }

        var now = DateTime.UtcNow;
        bool live = ObsTools.GetBool(stream, "outputActive");
        bool reconnecting = ObsTools.GetBool(stream, "outputReconnecting");
        long bytes = (long)ObsTools.GetDouble(stream, "outputBytes");
        long netSkip = (long)ObsTools.GetDouble(stream, "outputSkippedFrames");
        long netTotal = (long)ObsTools.GetDouble(stream, "outputTotalFrames");
        long rSkip = (long)ObsTools.GetDouble(stats, "renderSkippedFrames");
        long rTotal = (long)ObsTools.GetDouble(stats, "renderTotalFrames");
        long oSkip = (long)ObsTools.GetDouble(stats, "outputSkippedFrames");
        long oTotal = (long)ObsTools.GetDouble(stats, "outputTotalFrames");

        double kbps = 0, drop = 0, rLag = 0, oLag = 0;
        bool hasPrev = _at != DateTime.MinValue && live;
        if (hasPrev)
        {
            var secs = (now - _at).TotalSeconds;
            if (secs > 0 && bytes >= _bytes) kbps = (bytes - _bytes) * 8 / 1000.0 / secs;
            drop = Pct(netSkip - _netSkip, netTotal - _netTotal);
        }
        if (_at != DateTime.MinValue)
        {
            rLag = Pct(rSkip - _rSkip, rTotal - _rTotal);
            oLag = Pct(oSkip - _oSkip, oTotal - _oTotal);
        }

        _at = now;
        _bytes = bytes; _netSkip = netSkip; _netTotal = netTotal;
        _rSkip = rSkip; _rTotal = rTotal; _oSkip = oSkip; _oTotal = oTotal;

        var snap = new HealthSnapshot(
            live, reconnecting, ObsTools.ShortTimecode(ObsTools.GetString(stream, "outputTimecode")),
            kbps, drop, ObsTools.GetDouble(stats, "cpuUsage"), ObsTools.GetDouble(stats, "activeFps"),
            rLag, oLag, recording, recTime);

        string? alert = null;
        if (live)
        {
            if (reconnecting)
                alert = "A live caiu e o OBS está tentando reconectar. Confira a sua internet.";
            else if (drop > 3)
                alert = $"Internet instável: {drop:0.#}% dos quadros não chegaram. Feche downloads ou reduza o bitrate.";
            else if (oLag > 3)
                alert = $"O codificador está sobrecarregado ({oLag:0.#}% de quadros pulados). Reduza a resolução, o FPS ou a predefinição.";
            else if (rLag > 3)
                alert = $"A placa de vídeo está sobrecarregada ({rLag:0.#}% de quadros pulados). Limite o FPS do jogo.";
        }

        if (alert != null)
        {
            if ((now - _lastAlert).TotalSeconds < 60) alert = null;
            else _lastAlert = now;
        }
        return (snap, alert);
    }

    static double Pct(long part, long total) => total > 30 ? Math.Max(0, part) * 100.0 / total : 0;

    public void Reset() => _at = DateTime.MinValue;
}

/// <summary>Começar live: checklist, inicia a transmissão, mostra a contagem e troca para a cena principal.</summary>
public sealed class GoLiveRunner
{
    public const string CountdownInput = "NTOobs Contagem";
    CancellationTokenSource? _cts;

    public bool CountingDown => _cts != null;

    /// <summary>Pula a contagem e vai direto para a cena principal.</summary>
    public void Skip() => _cts?.Cancel();

    public static async Task<List<string>> ChecklistAsync(ObsClient obs, Settings s)
    {
        var problems = new List<string>();
        var scenes = await ObsTools.GetSceneNamesAsync(obs);
        if (s.LiveStartScene.Length > 0 && !scenes.Contains(s.LiveStartScene))
            problems.Add($"A cena de início \"{s.LiveStartScene}\" não existe no OBS.");
        if (s.LiveMainScene.Length > 0 && !scenes.Contains(s.LiveMainScene))
            problems.Add($"A cena principal \"{s.LiveMainScene}\" não existe no OBS.");

        if (s.LiveMic.Length > 0)
        {
            try
            {
                var m = await obs.RequestAsync("GetInputMute", new { inputName = s.LiveMic });
                if (ObsTools.GetBool(m, "inputMuted")) problems.Add($"O microfone \"{s.LiveMic}\" está MUTADO.");
            }
            catch
            {
                problems.Add($"Não achei o microfone \"{s.LiveMic}\" no OBS.");
            }
        }

        try
        {
            var st = await obs.RequestAsync("GetStats");
            var disk = ObsTools.GetDouble(st, "availableDiskSpace"); // MB
            if (s.LiveRecord && disk > 0 && disk < 5000)
                problems.Add($"Pouco espaço em disco para gravar ({disk / 1024:0.#} GB livres).");
        }
        catch { }

        return problems;
    }

    public async Task StartAsync(ObsClient obs, Settings s, Action<string> log)
    {
        if (_cts != null)
        {
            Skip();
            return;
        }

        var stream = await obs.RequestAsync("GetStreamStatus");
        if (!ObsTools.GetBool(stream, "outputActive"))
        {
            await obs.RequestAsync("StartStream");
            log("Transmissão iniciada.");
        }

        if (s.LiveRecord)
        {
            try
            {
                var rec = await obs.RequestAsync("GetRecordStatus");
                if (!ObsTools.GetBool(rec, "outputActive"))
                {
                    await obs.RequestAsync("StartRecord");
                    log("Gravação iniciada.");
                }
            }
            catch (Exception ex)
            {
                log("Não consegui iniciar a gravação: " + ex.Message);
            }
        }

        if (s.LiveStartScene.Length > 0 && s.LiveCountdown > 0)
        {
            using var cts = new CancellationTokenSource();
            _cts = cts;
            try
            {
                await obs.RequestAsync("SetCurrentProgramScene", new { sceneName = s.LiveStartScene });
                await ObsTools.EnsureTextAsync(obs, s.LiveStartScene, CountdownInput, Countdown(s.LiveCountdown), true, s.TextStyle.Scaled(1.5));

                for (int left = s.LiveCountdown; left > 0; left--)
                {
                    await ObsTools.SetTextAsync(obs, CountdownInput, Countdown(left));
                    try { await Task.Delay(1000, cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
                await ObsTools.SetTextAsync(obs, CountdownInput, "Começando!");
            }
            finally
            {
                _cts = null;
            }
        }

        if (s.LiveMainScene.Length > 0)
            await obs.RequestAsync("SetCurrentProgramScene", new { sceneName = s.LiveMainScene });
        log("Você está ao vivo!");
    }

    static string Countdown(int seconds) => $"Começando em {seconds / 60}:{seconds % 60:00}";

    public static async Task StopAsync(ObsClient obs, Action<string> log)
    {
        try { await obs.RequestAsync("StopStream"); log("Transmissão encerrada."); }
        catch (Exception ex) { log("Transmissão: " + ex.Message); }
        try
        {
            var rec = await obs.RequestAsync("GetRecordStatus");
            if (ObsTools.GetBool(rec, "outputActive"))
            {
                await obs.RequestAsync("StopRecord");
                log("Gravação encerrada.");
            }
        }
        catch { }
    }
}
