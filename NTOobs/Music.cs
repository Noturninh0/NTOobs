using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NTOobs;

/// <summary>Kind: "yt" (YouTube) ou "sp" (Spotify). Type: playlist, video, album, track...</summary>
public sealed record MusicLink(string Kind, string Type, string Id);

public static class MusicLinks
{
    static string? Query(Uri uri, string key)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv[0] == key) return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return null;
    }

    public static bool TryParse(string? input, out MusicLink? link, out string error)
    {
        link = null;
        error = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            error = "Cole o link de uma playlist do YouTube ou do Spotify.";
            return false;
        }

        var m = Regex.Match(text, @"^spotify:(playlist|album|track|episode|show):([A-Za-z0-9]+)$");
        if (m.Success)
        {
            link = new MusicLink("sp", m.Groups[1].Value, m.Groups[2].Value);
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            error = "Esse link não parece válido. Cole o endereço completo (começando com https://).";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        if (host.EndsWith("spotify.com"))
        {
            var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segs.Length - 1; i++)
            {
                if (segs[i] is "playlist" or "album" or "track" or "episode" or "show"
                    && Regex.IsMatch(segs[i + 1], "^[A-Za-z0-9]+$"))
                {
                    link = new MusicLink("sp", segs[i], segs[i + 1]);
                    return true;
                }
            }
            error = "Não encontrei uma playlist nesse link do Spotify.";
            return false;
        }

        if (host.EndsWith("youtube.com") || host == "youtu.be")
        {
            var list = Query(uri, "list");
            if (list != null && Regex.IsMatch(list, "^[A-Za-z0-9_-]+$"))
            {
                link = new MusicLink("yt", "playlist", list);
                return true;
            }

            var video = host == "youtu.be"
                ? uri.AbsolutePath.Trim('/')
                : Query(uri, "v");
            if (video != null && Regex.IsMatch(video, "^[A-Za-z0-9_-]{6,}$"))
            {
                link = new MusicLink("yt", "video", video);
                return true;
            }

            error = "Não encontrei uma playlist nesse link do YouTube.";
            return false;
        }

        error = "Só links do YouTube ou do Spotify são aceitos.";
        return false;
    }
}

/// <summary>
/// Servidor local (só 127.0.0.1). O OBS carrega a página do player daqui e a página
/// conversa de volta: pergunta se há comandos (pausar/retomar, volume) e informa o estado.
/// Depois que a página abriu, a música continua tocando mesmo com o NTOobs fechado.
/// </summary>
public sealed class MusicServer : IDisposable
{
    readonly object _lock = new();
    HttpListener? _listener;
    int _seq;
    string _action = "";
    DateTime _lastBeat = DateTime.MinValue;

    public int Port { get; private set; }
    public int Volume { get; set; } = 60;
    public string State { get; private set; } = "";
    public string Title { get; private set; } = "";

    /// <summary>Segundos desde o último sinal de vida da página do player.</summary>
    public double SecondsSinceBeat => _lastBeat == DateTime.MinValue
        ? double.MaxValue
        : (DateTime.UtcNow - _lastBeat).TotalSeconds;

    public event Action? Changed;

    public bool Start(int firstPort)
    {
        for (int p = firstPort; p < firstPort + 10; p++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{p}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException)
            {
                listener.Close();
                continue;
            }

            _listener = listener;
            Port = p;
            _ = Task.Run(() => LoopAsync(listener));
            return true;
        }
        return false;
    }

    public string BuildPlayerUrl(MusicLink link, bool loop, bool shuffle) =>
        $"http://127.0.0.1:{Port}/player?k={link.Kind}&t={link.Type}&id={Uri.EscapeDataString(link.Id)}" +
        $"&l={(loop ? 1 : 0)}&s={(shuffle ? 1 : 0)}";

    public void Command(string action)
    {
        lock (_lock)
        {
            _seq++;
            _action = action;
        }
    }

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        Command("volume");
    }

    async Task LoopAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            switch (ctx.Request.Url?.AbsolutePath)
            {
                case "/player":
                    Write(ctx, "text/html; charset=utf-8", PlayerHtml);
                    break;

                case "/cmd":
                    int seq;
                    string action;
                    lock (_lock)
                    {
                        seq = _seq;
                        action = _action;
                    }
                    Write(ctx, "application/json", $"{{\"seq\":{seq},\"action\":\"{action}\",\"volume\":{Volume}}}");
                    break;

                case "/report":
                    Report(ctx.Request.QueryString["state"] ?? "", ctx.Request.QueryString["title"] ?? "");
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    break;
            }
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { }
        }
    }

    static void Write(HttpListenerContext ctx, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    void Report(string state, string title)
    {
        bool changed;
        lock (_lock)
        {
            _lastBeat = DateTime.UtcNow;
            changed = state != State || title != Title;
            State = state;
            Title = title;
        }
        if (changed) Changed?.Invoke();
    }

    public void Dispose()
    {
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    // Página carregada pelo OBS (fonte de navegador). Fica tocando por conta própria.
    const string PlayerHtml = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>NTOobs Música</title>
<style>
html,body{margin:0;height:100%;background:#000;overflow:hidden}
#p{position:absolute;left:0;top:0;width:100%;height:100%}
iframe{width:100%;height:100%;border:0}
</style>
</head>
<body>
<div id="p"></div>
<script>
(function () {
  var q = new URLSearchParams(location.search);
  var kind = q.get('k'), type = q.get('t') || 'playlist', id = q.get('id') || '';
  var loop = q.get('l') !== '0', shuffle = q.get('s') === '1';
  var vol = 60, lastSeq = -1, api = null, state = 'iniciando', title = '', shuffled = false;

  function report() {
    fetch('/report?state=' + encodeURIComponent(state) + '&title=' + encodeURIComponent(title), { cache: 'no-store' })
      .catch(function () {});
  }
  function setState(s) { if (s !== state) { state = s; report(); } }

  function handle(c) {
    if (!api) return;
    try {
      if (kind === 'yt') {
        if (c.action === 'toggle') { if (state === 'tocando') api.pauseVideo(); else api.playVideo(); }
        else if (c.action === 'pause') api.pauseVideo();
        else if (c.action === 'play') api.playVideo();
        else if (c.action === 'next') api.nextVideo();
        else if (c.action === 'volume') { vol = c.volume; api.setVolume(vol); }
      } else {
        if (c.action === 'toggle') api.togglePlay();
        else if (c.action === 'pause') api.pause();
        else if (c.action === 'play') api.resume();
      }
    } catch (e) {}
  }

  function poll() {
    fetch('/cmd', { cache: 'no-store' })
      .then(function (r) { return r.json(); })
      .then(function (c) {
        if (lastSeq === -1) { lastSeq = c.seq; }
        else if (c.seq !== lastSeq) { lastSeq = c.seq; handle(c); }
      })
      .catch(function () {});
  }

  function startYT() {
    window.onYouTubeIframeAPIReady = function () {
      var vars = { autoplay: 1, controls: 0, rel: 0, playsinline: 1, origin: location.origin };
      var opts = {
        width: '100%', height: '100%', playerVars: vars,
        events: {
          onReady: function (e) { e.target.setVolume(vol); e.target.playVideo(); },
          onStateChange: function (e) {
            var S = YT.PlayerState;
            if (e.data === S.PLAYING) {
              setState('tocando');
              if (type === 'playlist' && shuffle && !shuffled) {
                shuffled = true;
                try { e.target.setShuffle(true); } catch (x) {}
              }
            } else if (e.data === S.PAUSED) setState('pausado');
            else if (e.data === S.BUFFERING) setState('carregando');
            else if (e.data === S.ENDED) {
              setState('terminou');
              if (type === 'video' && loop) { e.target.seekTo(0); e.target.playVideo(); }
            }
            try {
              var t = e.target.getVideoData().title;
              if (t && t !== title) { title = t; report(); }
            } catch (x) {}
          },
          onError: function (e) { setState('erro ' + e.data); }
        }
      };
      if (type === 'playlist') { vars.listType = 'playlist'; vars.list = id; if (loop) vars.loop = 1; }
      else { opts.videoId = id; }
      api = new YT.Player('p', opts);
    };
    var s = document.createElement('script');
    s.src = 'https://www.youtube.com/iframe_api';
    document.head.appendChild(s);
  }

  function startSP() {
    window.onSpotifyIframeApiReady = function (IFrameAPI) {
      IFrameAPI.createController(document.getElementById('p'),
        { uri: 'spotify:' + type + ':' + id, width: '100%', height: '100%' },
        function (c) {
          api = c;
          c.addListener('ready', function () { c.play(); });
          c.addListener('playback_update', function (e) {
            var d = (e && e.data) || {};
            if (d.isBuffering) setState('carregando');
            else setState(d.isPaused ? 'pausado' : 'tocando');
          });
        });
    };
    var s = document.createElement('script');
    s.src = 'https://open.spotify.com/embed/iframe-api/v1';
    s.async = true;
    document.body.appendChild(s);
  }

  fetch('/cmd', { cache: 'no-store' })
    .then(function (r) { return r.json(); })
    .then(function (c) { vol = c.volume; lastSeq = c.seq; })
    .catch(function () {})
    .then(function () {
      report();
      setInterval(report, 4000);
      setInterval(poll, 400);
      if (kind === 'sp') startSP(); else startYT();
    });
})();
</script>
</body>
</html>
""";
}

/// <summary>Cria e mantém no OBS as fontes de música (YouTube: navegador; Spotify: captura do aplicativo).</summary>
public static class MusicObs
{
    public const string InputName = "NTOobs Música";
    public const string SpotifyInputName = "NTOobs Spotify";

    static async Task<(List<string> Scenes, string Current)> GetScenesAsync(ObsClient obs)
    {
        var list = await obs.RequestAsync("GetSceneList");
        var scenes = list.GetProperty("scenes").EnumerateArray()
            .Select(s => s.GetProperty("sceneName").GetString() ?? "")
            .Where(n => n.Length > 0)
            .ToList();
        if (scenes.Count == 0)
            throw new InvalidOperationException("O OBS não tem nenhuma cena.");

        var current = list.TryGetProperty("currentProgramSceneName", out var c)
            ? c.GetString() ?? scenes[0]
            : scenes[0];
        return (scenes, current);
    }

    static async Task<bool> InputExistsAsync(ObsClient obs, string name)
    {
        try
        {
            await obs.RequestAsync("GetInputSettings", new { inputName = name });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>YouTube: fonte de navegador com o player, com o áudio no mixer.</summary>
    public static async Task EnsureAsync(ObsClient obs, string url, Action<string> log)
    {
        bool exists = false;
        string? currentUrl = null;
        try
        {
            var d = await obs.RequestAsync("GetInputSettings", new { inputName = InputName });
            exists = true;
            if (d.TryGetProperty("inputSettings", out var s) && s.TryGetProperty("url", out var u))
                currentUrl = u.GetString();
        }
        catch (InvalidOperationException)
        {
            exists = false; // a fonte ainda não existe
        }

        var settings = new
        {
            url,
            width = 640,
            height = 360,
            fps = 30,
            reroute_audio = true,      // "Controlar áudio pelo OBS": o som aparece no mixer
            shutdown = false,          // não desliga a fonte quando ela sai da tela
            restart_when_active = false
        };

        var (scenes, current) = await GetScenesAsync(obs);

        if (!exists)
        {
            var created = await obs.RequestAsync("CreateInput", new
            {
                sceneName = current,
                inputName = InputName,
                inputKind = "browser_source",
                inputSettings = settings,
                sceneItemEnabled = true
            });
            await MoveOffCanvasAsync(obs, current, created.GetProperty("sceneItemId").GetInt32(), log);
            log("Fonte de música criada no OBS.");
        }
        else if (currentUrl != url)
        {
            await obs.RequestAsync("SetInputSettings", new { inputName = InputName, inputSettings = settings, overlay = true });
            log("Playlist atualizada no OBS.");
        }

        await EnsureInAllScenesAsync(obs, InputName, scenes, true, log);
        await UnmuteAsync(obs, InputName);
    }

    /// <summary>Spotify: captura só o áudio do aplicativo Spotify (Captura de áudio de aplicativo).</summary>
    public static async Task EnsureSpotifyAsync(ObsClient obs, Action<string> log)
    {
        var (scenes, current) = await GetScenesAsync(obs);

        if (!await InputExistsAsync(obs, SpotifyInputName))
        {
            await obs.RequestAsync("CreateInput", new
            {
                sceneName = current,
                inputName = SpotifyInputName,
                inputKind = "wasapi_process_output_capture",
                inputSettings = new
                {
                    window = "Spotify:Chrome_WidgetWin_0:Spotify.exe",
                    priority = 2 // casar pelo executável (Spotify.exe), seja qual for o título da janela
                },
                sceneItemEnabled = true
            });
            log("Captura de áudio do Spotify criada no OBS.");
        }

        await EnsureInAllScenesAsync(obs, SpotifyInputName, scenes, false, log);
        await UnmuteAsync(obs, SpotifyInputName);
    }

    // A fonte precisa estar em todas as cenas para o áudio não cortar ao trocar de cena.
    static async Task EnsureInAllScenesAsync(ObsClient obs, string name, List<string> scenes, bool offCanvas, Action<string> log)
    {
        foreach (var scene in scenes)
        {
            if (await HasItemAsync(obs, scene, name)) continue;
            var item = await obs.RequestAsync("CreateSceneItem", new
            {
                sceneName = scene,
                sourceName = name,
                sceneItemEnabled = true
            });
            if (offCanvas)
                await MoveOffCanvasAsync(obs, scene, item.GetProperty("sceneItemId").GetInt32(), log);
            log($"Música adicionada à cena \"{scene}\".");
        }
    }

    static async Task UnmuteAsync(ObsClient obs, string name)
    {
        try { await obs.RequestAsync("SetInputMute", new { inputName = name, inputMuted = false }); }
        catch { }
    }

    public static async Task RemoveAsync(ObsClient obs, string name)
    {
        try { await obs.RequestAsync("RemoveInput", new { inputName = name }); }
        catch { } // se não existir, está tudo certo
    }

    public static async Task RefreshAsync(ObsClient obs)
    {
        await obs.RequestAsync("PressInputPropertiesButton", new { inputName = InputName, propertyName = "refreshnocache" });
    }

    static async Task<bool> HasItemAsync(ObsClient obs, string scene, string name)
    {
        try
        {
            await obs.RequestAsync("GetSceneItemId", new { sceneName = scene, sourceName = name });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // O vídeo do player não deve aparecer na transmissão: só o áudio importa.
    static async Task MoveOffCanvasAsync(ObsClient obs, string scene, int sceneItemId, Action<string> log)
    {
        try
        {
            await obs.RequestAsync("SetSceneItemTransform", new
            {
                sceneName = scene,
                sceneItemId,
                sceneItemTransform = new { positionX = -5000.0, positionY = -5000.0 }
            });
        }
        catch (Exception ex)
        {
            log("Aviso: não consegui esconder o vídeo da música fora da tela: " + ex.Message);
        }
    }
}
