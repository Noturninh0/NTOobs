using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Windows;
using System.Windows.Controls;

namespace NTOobs;

// Twitch: chat, comandos e alertas na tela.
public partial class MainWindow
{
    volatile string _twitchState = "";
    readonly Dictionary<string, DateTime> _cmdCooldown = new();

    void InitTwitchUi()
    {
        TxtTwitchChannel.Text = _settings.TwitchChannel;
        ChkTwitchEnabled.IsChecked = _settings.TwitchEnabled;
        TxtTwitchClientId.Text = _settings.TwitchClientId;
        ChkAlertFollow.IsChecked = _settings.AlertFollow;
        ChkAlertSub.IsChecked = _settings.AlertSub;
        ChkAlertRaid.IsChecked = _settings.AlertRaid;
        ChkAlertBits.IsChecked = _settings.AlertBits;
        ChkCmdMusic.IsChecked = _settings.CmdMusic;
        ChkCmdMark.IsChecked = _settings.CmdMark;
        _alerts.SoundFile = _settings.AlertSound;
        TxtTwitchRedirect.Text = !_musicReady ? "(o servidor local não iniciou)"
            : _music.LocalhostOk ? $"http://localhost:{_music.Port}/twitch"
            : "(o Windows não liberou o endereço localhost: o login não vai funcionar neste PC)";
        RefreshAlertSoundLabel();
        RefreshCommands();
        RefreshTwitchLoginLabel();

        _twitch.Status += s => Dispatcher.InvokeAsync(() =>
        {
            TxtTwitchStatus.Text = s;
            Log("Twitch: " + s);
        });
        _twitch.Chat += c => Dispatcher.InvokeAsync(() => HandleChatAsync(c));
        _twitch.Alert += (kind, title, detail) => Dispatcher.InvokeAsync(() => OnTwitchAlert(kind, title, detail));

        if (_settings.TwitchEnabled && _settings.TwitchChannel.Length > 0)
            _ = StartTwitchAsync();
    }

    void SaveTwitch()
    {
        _settings.TwitchChannel = TxtTwitchChannel.Text.Trim().TrimStart('#');
        _settings.TwitchEnabled = ChkTwitchEnabled.IsChecked == true;
        _settings.TwitchClientId = TxtTwitchClientId.Text.Trim();
        _settings.AlertFollow = ChkAlertFollow.IsChecked == true;
        _settings.AlertSub = ChkAlertSub.IsChecked == true;
        _settings.AlertRaid = ChkAlertRaid.IsChecked == true;
        _settings.AlertBits = ChkAlertBits.IsChecked == true;
        _settings.CmdMusic = ChkCmdMusic.IsChecked == true;
        _settings.CmdMark = ChkCmdMark.IsChecked == true;
        SaveSettings();
    }

    void Twitch_Changed(object sender, RoutedEventArgs e) => SaveTwitch();
    void Twitch_LostFocus(object sender, RoutedEventArgs e) => SaveTwitch();

    void RefreshTwitchLoginLabel()
    {
        TxtTwitchLogin.Text = _settings.TwitchToken.Length > 0
            ? $"Conectado como {_settings.TwitchLogin}"
            : "Sem login: o chat fica só leitura e sem alertas de follow.";
    }

    async Task StartTwitchAsync()
    {
        var channel = _settings.TwitchChannel.Trim().TrimStart('#');
        if (channel.Length == 0)
        {
            TxtTwitchStatus.Text = "Digite o nome do canal.";
            return;
        }

        string token = _settings.TwitchToken, login = "", userId = "";
        if (token.Length > 0)
        {
            try
            {
                var v = await TwitchClient.ValidateAsync(token);
                login = v.Login;
                userId = v.UserId;
                _settings.TwitchLogin = login;
                _settings.TwitchUserId = userId;
            }
            catch (Exception ex)
            {
                Log("Twitch: " + ex.Message);
                TxtTwitchLogin.Text = ex.Message;
                token = "";
            }
        }

        // alertas de follow só no seu próprio canal
        bool own = login.Length > 0 && login.Equals(channel, StringComparison.OrdinalIgnoreCase);
        _twitch.Start(channel, token, login, own ? _settings.TwitchClientId : "", own ? userId : "");
        if (token.Length > 0 && !own)
            Log("Twitch: os alertas de follow só funcionam no seu próprio canal.");

        TxtTwitchStatus.Text = "Conectando ao chat...";
        BtnTwitchConnect.Content = "Desconectar";
    }

    async void BtnTwitchConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_twitch.Running)
        {
            _twitch.Stop();
            TxtTwitchStatus.Text = "Desconectado";
            BtnTwitchConnect.Content = "Conectar";
            return;
        }
        SaveTwitch();
        await StartTwitchAsync();
    }

    void OnTwitchAlert(string kind, string title, string detail)
    {
        bool on = kind switch
        {
            "follow" => _settings.AlertFollow,
            "sub" => _settings.AlertSub,
            "raid" => _settings.AlertRaid,
            "bits" => _settings.AlertBits,
            _ => true
        };
        Log("Twitch: " + title);
        if (on) _alerts.Push(kind, title, detail);
    }

    static string NormalizeCommand(string s) => "!" + s.Trim().TrimStart('!').ToLowerInvariant();

    string CurrentTrackText()
    {
        if (!_settings.MusicEnabled) return "Nenhuma música tocando agora.";
        if (IsSpotifyMode)
        {
            var st = SpotifyDesktop.GetState();
            return st.Playing ? "🎵 Tocando agora: " + st.Track : "A música está pausada agora.";
        }
        return _music.Title.Length > 0 ? "🎵 Tocando agora: " + _music.Title : "Nenhuma música tocando agora.";
    }

    async Task HandleChatAsync(TwitchChat c)
    {
        try
        {
            var text = c.Text.Trim();
            if (!text.StartsWith('!')) return;

            var space = text.IndexOf(' ');
            var cmd = (space < 0 ? text : text[..space]).ToLowerInvariant();
            var rest = space < 0 ? "" : text[(space + 1)..].Trim();

            if (_cmdCooldown.TryGetValue(cmd, out var last) && (DateTime.UtcNow - last).TotalSeconds < 5) return;

            string? reply = null;
            if (_settings.CmdMusic && cmd is "!musica" or "!música" or "!music" or "!song")
            {
                reply = CurrentTrackText();
            }
            else if (_settings.CmdMark && cmd is "!marcar" or "!marca" or "!clip")
            {
                if (!c.IsMod) return;
                await AddMarkerAsync((rest.Length > 0 ? rest + " " : "") + $"(chat: {c.DisplayName})");
                reply = $"Momento marcado, {c.DisplayName}!";
            }
            else
            {
                var custom = _settings.TwitchCommands.FirstOrDefault(x => NormalizeCommand(x.Command) == cmd);
                if (custom != null) reply = custom.Response.Replace("{user}", c.DisplayName);
            }

            if (reply == null) return;
            _cmdCooldown[cmd] = DateTime.UtcNow;
            if (_twitch.CanSend) await _twitch.SendChatAsync(reply);
            else Log($"Twitch: {cmd} de {c.DisplayName}. Entre com a Twitch para o NTOobs responder no chat.");
        }
        catch (Exception ex)
        {
            Log("Twitch: " + ex.Message);
        }
    }

    // ---------- Alertas no OBS ----------

    async void BtnAlertsObs_Click(object sender, RoutedEventArgs e)
    {
        if (!_obs.IsConnected)
        {
            MessageBox.Show(this, "Conecte ao OBS primeiro (aba Conexão).", "NTOobs");
            return;
        }
        if (!_musicReady)
        {
            MessageBox.Show(this, "O servidor local do NTOobs não iniciou. Feche outros programas e abra o NTOobs de novo.", "NTOobs");
            return;
        }

        const string name = "NTOobs Alertas";
        try
        {
            var (w, h) = await ObsTools.GetCanvasAsync(_obs);
            var settings = new
            {
                url = $"http://127.0.0.1:{_music.Port}/alerts",
                width = w,
                height = h,
                fps = 30,
                reroute_audio = true,
                shutdown = false,
                restart_when_active = false
            };
            var (scenes, current) = await MusicObs.GetScenesAsync(_obs);
            if (!await MusicObs.InputExistsAsync(_obs, name))
            {
                await _obs.RequestAsync("CreateInput", new
                {
                    sceneName = current,
                    inputName = name,
                    inputKind = "browser_source",
                    inputSettings = settings,
                    sceneItemEnabled = true
                });
            }
            else
            {
                await _obs.RequestAsync("SetInputSettings", new { inputName = name, inputSettings = settings, overlay = true });
            }
            await MusicObs.EnsureInAllScenesAsync(_obs, name, scenes, false, Log);
            await MusicObs.UnmuteAsync(_obs, name);
            Log("Alertas no OBS: fonte \"NTOobs Alertas\" em todas as cenas. Clique em Testar alerta.");
        }
        catch (Exception ex)
        {
            Log("Erro ao adicionar os alertas: " + ex.Message);
        }
    }

    void BtnAlertTest_Click(object sender, RoutedEventArgs e)
    {
        _alerts.Push("test", "Alerta de teste!", "Se você está vendo isso no OBS, os alertas funcionam.");
        Log("Alerta de teste enviado.");
    }

    void RefreshAlertSoundLabel() =>
        TxtAlertSound.Text = File.Exists(_settings.AlertSound) ? "Som: " + Path.GetFileName(_settings.AlertSound) : "Sem som";

    void BtnAlertSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Som dos alertas",
            Filter = "Áudio (mp3, wav, ogg)|*.mp3;*.wav;*.ogg"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _settings.AlertSound = Soundboard.Import(dialog.FileName);
            _alerts.SoundFile = _settings.AlertSound;
            SaveSettings();
            RefreshAlertSoundLabel();
            RefreshAlertKinds();
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    void BtnAlertSoundClear_Click(object sender, RoutedEventArgs e)
    {
        _settings.AlertSound = "";
        _alerts.SoundFile = "";
        SaveSettings();
        RefreshAlertSoundLabel();
        RefreshAlertKinds();
    }

    // ---------- Comandos ----------

    void RefreshCommands() => ListCommands.ItemsSource = _settings.TwitchCommands.ToList();

    void BtnCommandAdd_Click(object sender, RoutedEventArgs e)
    {
        var cmd = NormalizeCommand(TxtCmdName.Text);
        var response = TxtCmdResponse.Text.Trim();
        if (cmd.Length < 2 || cmd.Contains(' ') || response.Length == 0)
        {
            MessageBox.Show(this, "Preencha o comando (ex.: !discord, sem espaços) e a resposta.", "NTOobs");
            return;
        }
        _settings.TwitchCommands.RemoveAll(x => NormalizeCommand(x.Command) == cmd);
        _settings.TwitchCommands.Add(new TwitchCommand { Command = cmd, Response = response });
        TxtCmdName.Text = "";
        TxtCmdResponse.Text = "";
        SaveSettings();
        RefreshCommands();
    }

    void CommandRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TwitchCommand c }) return;
        _settings.TwitchCommands.Remove(c);
        SaveSettings();
        RefreshCommands();
    }

    // ---------- Login ----------

    void BtnTwitchConsole_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://dev.twitch.tv/console/apps/create");

    void BtnTwitchCopyRedirect_Click(object sender, RoutedEventArgs e) => CopyText(TxtTwitchRedirect.Text);

    void BtnTwitchLogin_Click(object sender, RoutedEventArgs e)
    {
        SaveTwitch();
        if (!_musicReady)
        {
            MessageBox.Show(this, "O servidor local do NTOobs não iniciou. Feche outros programas e abra o NTOobs de novo.", "NTOobs");
            return;
        }
        if (!_music.LocalhostOk)
        {
            MessageBox.Show(this, "O Windows não liberou o endereço localhost para o NTOobs, então o login da Twitch não funciona neste PC. O chat e os alertas de sub, raid e bits funcionam sem login.", "NTOobs");
            return;
        }
        if (_settings.TwitchClientId.Length == 0)
        {
            MessageBox.Show(this, "Cole primeiro o ID do cliente (Client ID) do seu aplicativo da Twitch.", "NTOobs");
            return;
        }
        _twitchState = TwitchClient.NewState();
        OpenUrl(TwitchClient.AuthorizeUrl(_settings.TwitchClientId, TxtTwitchRedirect.Text, _twitchState));
        TxtTwitchLogin.Text = "Autorize no navegador que abriu...";
    }

    async void BtnTwitchLogout_Click(object sender, RoutedEventArgs e)
    {
        _settings.TwitchToken = "";
        _settings.TwitchLogin = "";
        _settings.TwitchUserId = "";
        SaveSettings();
        RefreshTwitchLoginLabel();
        if (_twitch.Running)
        {
            _twitch.Stop();
            await StartTwitchAsync();
        }
    }

    async Task CompleteTwitchLoginAsync(string token)
    {
        try
        {
            var v = await TwitchClient.ValidateAsync(token);
            _settings.TwitchToken = token;
            _settings.TwitchLogin = v.Login;
            _settings.TwitchUserId = v.UserId;
            if (TxtTwitchChannel.Text.Trim().Length == 0) TxtTwitchChannel.Text = v.Login;
            SaveTwitch();
            RefreshTwitchLoginLabel();
            Log($"Twitch: conectado como {v.Login}.");

            _twitch.Stop();
            await StartTwitchAsync();
            Activate();
        }
        catch (Exception ex)
        {
            Log("Twitch: " + ex.Message);
            TxtTwitchLogin.Text = ex.Message;
        }
    }

    /// <summary>Páginas extras do servidor local: alertas e retorno do login da Twitch.</summary>
    bool HandleExtraHttp(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "";
        if (path.StartsWith("/alerts", StringComparison.Ordinal)) return _alerts.Handle(ctx);

        if (path == "/twitch")
        {
            MusicServer.Write(ctx, "text/html; charset=utf-8", TwitchLoginPage.Html);
            return true;
        }

        if (path == "/twitch/token" && ctx.Request.HttpMethod == "POST")
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();
            var form = HttpUtility.ParseQueryString(body);
            var token = form["token"] ?? "";
            var state = form["state"] ?? "";
            var expected = _twitchState;

            if (token.Length == 0 || expected.Length == 0 || state != expected)
            {
                MusicServer.Write(ctx, "text/plain; charset=utf-8",
                    "Login recusado. Use o botão Entrar com a Twitch dentro do NTOobs.");
                return true;
            }

            _twitchState = "";
            Dispatcher.InvokeAsync(() => CompleteTwitchLoginAsync(token));
            MusicServer.Write(ctx, "text/plain; charset=utf-8", "Pronto! Pode fechar esta aba e voltar para o NTOobs.");
            return true;
        }
        return false;
    }
}
