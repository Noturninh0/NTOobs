using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NTOobs;

public sealed record TwitchChat(string User, string DisplayName, string Text, bool IsMod, bool IsBroadcaster);

public sealed record TwitchAlert(long Id, string Kind, string Title, string Detail);

/// <summary>
/// Conexão com a Twitch. Sem login: lê o chat e os alertas de sub, raid e bits.
/// Com login: também responde no chat e recebe os alertas de follow (EventSub).
/// </summary>
public sealed class TwitchClient : IDisposable
{
    const string IrcUrl = "wss://irc-ws.chat.twitch.tv:443";
    const string EventSubUrl = "wss://eventsub.wss.twitch.tv/ws";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly SemaphoreSlim _sendLock = new(1, 1);
    CancellationTokenSource? _cts;
    ClientWebSocket? _irc;
    string _channel = "";
    string _token = "";
    string _login = "";

    public event Action<string>? Status;
    public event Action<TwitchChat>? Chat;
    public event Action<string, string, string>? Alert; // tipo, título, detalhe

    public bool CanSend => _token.Length > 0;
    public bool Running => _cts != null;

    public void Start(string channel, string token, string login, string clientId, string userId)
    {
        Stop();
        _channel = channel.Trim().TrimStart('#').ToLowerInvariant();
        _token = token;
        _login = login.ToLowerInvariant();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _ = Task.Run(() => IrcLoopAsync(ct));
        if (token.Length > 0 && clientId.Length > 0 && userId.Length > 0)
            _ = Task.Run(() => EventSubLoopAsync(token, clientId, userId, ct));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        try { _irc?.Abort(); } catch { }
        _irc = null;
    }

    public void Dispose() => Stop();

    // ---------- Chat (IRC) ----------

    async Task IrcLoopAsync(CancellationToken ct)
    {
        int wait = 2;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(IrcUrl), ct);
                _irc = ws;
                await SendRawAsync(ws, "CAP REQ :twitch.tv/tags twitch.tv/commands", ct);
                if (_token.Length > 0 && _login.Length > 0)
                {
                    await SendRawAsync(ws, "PASS oauth:" + _token, ct);
                    await SendRawAsync(ws, "NICK " + _login, ct);
                }
                else
                {
                    await SendRawAsync(ws, "NICK justinfan" + Random.Shared.Next(10000, 99999), ct);
                }
                await SendRawAsync(ws, "JOIN #" + _channel, ct);

                await ReadIrcAsync(ws, ct);
                wait = 2;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Status?.Invoke("Chat desconectado: " + ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_irc, ws)) _irc = null;
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); } catch { break; }
            wait = Math.Min(wait * 2, 60);
        }
    }

    async Task ReadIrcAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var pending = new StringBuilder();
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var text = pending.ToString();
            pending.Clear();
            foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (await HandleIrcLineAsync(ws, line, ct)) return; // RECONNECT
            }
        }
    }

    /// <summary>Trata uma linha. true = a Twitch pediu para reconectar.</summary>
    async Task<bool> HandleIrcLineAsync(ClientWebSocket ws, string line, CancellationToken ct)
    {
        var msg = IrcMessage.Parse(line);
        switch (msg.Command)
        {
            case "PING":
                await SendRawAsync(ws, "PONG :" + msg.Trailing, ct);
                break;

            case "RECONNECT":
                return true;

            case "001":
                Status?.Invoke(_token.Length > 0 ? $"Conectado ao chat como {_login}." : "Conectado ao chat (somente leitura).");
                break;

            case "JOIN":
                if (msg.Prefix.StartsWith(_login.Length > 0 ? _login + "!" : "justinfan", StringComparison.OrdinalIgnoreCase))
                    Status?.Invoke($"Lendo o chat de #{_channel}.");
                break;

            case "NOTICE":
                if (msg.Trailing.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                    Status?.Invoke("A Twitch recusou o login. Entre de novo na aba Twitch.");
                else if (msg.Trailing.Length > 0)
                    Status?.Invoke("Twitch: " + msg.Trailing);
                break;

            case "PRIVMSG":
            {
                var display = msg.Tag("display-name");
                var user = msg.Prefix.Split('!')[0];
                if (display.Length == 0) display = user;
                var badges = msg.Tag("badges");
                bool broadcaster = badges.Contains("broadcaster/");
                bool mod = broadcaster || badges.Contains("moderator/") || msg.Tag("mod") == "1";

                if (int.TryParse(msg.Tag("bits"), out var bits) && bits > 0)
                    Alert?.Invoke("bits", $"{display} mandou {bits} bits!", msg.Trailing);

                Chat?.Invoke(new TwitchChat(user, display, msg.Trailing, mod, broadcaster));
                break;
            }

            case "USERNOTICE":
                HandleUserNotice(msg);
                break;
        }
        return false;
    }

    void HandleUserNotice(IrcMessage msg)
    {
        var display = msg.Tag("display-name");
        switch (msg.Tag("msg-id"))
        {
            case "sub":
                Alert?.Invoke("sub", $"{display} se inscreveu!", msg.Trailing);
                break;
            case "resub":
                var months = msg.Tag("msg-param-cumulative-months");
                Alert?.Invoke("sub", $"{display} renovou a inscrição! ({months} meses)", msg.Trailing);
                break;
            case "subgift":
                Alert?.Invoke("sub", $"{display} deu um sub para {msg.Tag("msg-param-recipient-display-name")}!", "");
                break;
            case "submysterygift":
                Alert?.Invoke("sub", $"{display} deu {msg.Tag("msg-param-mass-gift-count")} subs para o chat!", "");
                break;
            case "raid":
                var raider = msg.Tag("msg-param-displayName");
                if (raider.Length == 0) raider = display;
                Alert?.Invoke("raid", $"Raid de {raider}!", $"{msg.Tag("msg-param-viewerCount")} pessoas chegando");
                break;
        }
    }

    public async Task SendChatAsync(string text)
    {
        var ws = _irc;
        if (ws == null || ws.State != WebSocketState.Open || _token.Length == 0) return;
        text = text.Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 450) text = text[..450];
        await SendRawAsync(ws, $"PRIVMSG #{_channel} :{text}", CancellationToken.None);
    }

    async Task SendRawAsync(ClientWebSocket ws, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _sendLock.WaitAsync(ct);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ---------- Follows (EventSub) ----------

    async Task EventSubLoopAsync(string token, string clientId, string userId, CancellationToken ct)
    {
        int wait = 2;
        var url = EventSubUrl;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            string? reconnectUrl = null;
            try
            {
                await ws.ConnectAsync(new Uri(url), ct);
                reconnectUrl = await ReadEventSubAsync(ws, token, clientId, userId, subscribe: url == EventSubUrl, ct);
                wait = 2;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                Status?.Invoke(ex.Message);
                break; // sem permissão: não adianta tentar de novo
            }
            catch (Exception ex)
            {
                Status?.Invoke("Alertas de follow desconectados: " + ex.Message);
            }

            if (reconnectUrl != null)
            {
                url = reconnectUrl; // a Twitch mudou de servidor: as inscrições continuam valendo
                continue;
            }
            url = EventSubUrl;
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); } catch { break; }
            wait = Math.Min(wait * 2, 60);
        }
    }

    /// <summary>Lê mensagens do EventSub. Retorna a URL de reconexão, se a Twitch pedir.</summary>
    async Task<string?> ReadEventSubAsync(ClientWebSocket ws, string token, string clientId, string userId, bool subscribe, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            using var doc = JsonDocument.Parse(ms.ToArray());
            var root = doc.RootElement;
            var type = root.GetProperty("metadata").GetProperty("message_type").GetString();
            var payload = root.GetProperty("payload");

            switch (type)
            {
                case "session_welcome":
                    if (subscribe)
                    {
                        var sessionId = payload.GetProperty("session").GetProperty("id").GetString() ?? "";
                        await SubscribeFollowAsync(token, clientId, userId, sessionId, ct);
                        Status?.Invoke("Alertas de follow ativos.");
                    }
                    break;

                case "session_reconnect":
                    return payload.GetProperty("session").GetProperty("reconnect_url").GetString();

                case "notification":
                    var subType = payload.GetProperty("subscription").GetProperty("type").GetString();
                    var ev = payload.GetProperty("event");
                    if (subType == "channel.follow")
                    {
                        var name = ev.TryGetProperty("user_name", out var n) ? n.GetString() ?? "" : "";
                        Alert?.Invoke("follow", $"{name} começou a seguir!", "");
                    }
                    break;

                case "revocation":
                    Status?.Invoke("A Twitch cancelou a permissão dos alertas de follow. Entre de novo na aba Twitch.");
                    break;
            }
        }
        return null;
    }

    static async Task SubscribeFollowAsync(string token, string clientId, string userId, string sessionId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            type = "channel.follow",
            version = "2",
            condition = new { broadcaster_user_id = userId, moderator_user_id = userId },
            transport = new { method = "websocket", session_id = sessionId }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Authorization", "Bearer " + token);
        req.Headers.Add("Client-Id", clientId);

        using var res = await Http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Conflict) return;

        var text = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Sem permissão para os alertas de follow. Entre de novo na aba Twitch. " + text);
        throw new InvalidOperationException($"Erro ao ativar alertas de follow ({(int)res.StatusCode}): {text}");
    }

    // ---------- Login ----------

    public static string AuthorizeUrl(string clientId, string redirectUri, string state) =>
        "https://id.twitch.tv/oauth2/authorize?response_type=token" +
        "&client_id=" + Uri.EscapeDataString(clientId) +
        "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
        "&scope=" + Uri.EscapeDataString("chat:read chat:edit moderator:read:followers") +
        "&state=" + Uri.EscapeDataString(state) +
        "&force_verify=true";

    /// <summary>Confere o token. Retorna (login, userId, clientId) ou lança erro.</summary>
    public static async Task<(string Login, string UserId, string ClientId)> ValidateAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        req.Headers.Add("Authorization", "OAuth " + token);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException("O login da Twitch expirou ou é inválido. Entre de novo.");

        using var doc = JsonDocument.Parse(text);
        var r = doc.RootElement;
        return (r.GetProperty("login").GetString() ?? "",
                r.GetProperty("user_id").GetString() ?? "",
                r.GetProperty("client_id").GetString() ?? "");
    }

    public static string NewState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
}

/// <summary>Mensagem IRC da Twitch: @tags :prefixo COMANDO parâmetros :texto</summary>
public sealed class IrcMessage
{
    public Dictionary<string, string> Tags { get; } = new();
    public string Prefix { get; private set; } = "";
    public string Command { get; private set; } = "";
    public List<string> Params { get; } = new();
    public string Trailing { get; private set; } = "";

    public string Tag(string key) => Tags.TryGetValue(key, out var v) ? v : "";

    public static IrcMessage Parse(string line)
    {
        var m = new IrcMessage();
        int i = 0;

        if (line.StartsWith('@'))
        {
            var sp = line.IndexOf(' ');
            if (sp < 0) return m;
            foreach (var pair in line[1..sp].Split(';'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) m.Tags[pair] = "";
                else m.Tags[pair[..eq]] = Unescape(pair[(eq + 1)..]);
            }
            i = sp + 1;
        }

        if (i < line.Length && line[i] == ':')
        {
            var sp = line.IndexOf(' ', i);
            if (sp < 0) return m;
            m.Prefix = line[(i + 1)..sp];
            i = sp + 1;
        }

        var trail = line.IndexOf(" :", i, StringComparison.Ordinal);
        var head = trail >= 0 ? line[i..trail] : line[i..];
        if (trail >= 0) m.Trailing = line[(trail + 2)..];
        else if (head.StartsWith(':')) { m.Trailing = head[1..]; head = ""; }

        var parts = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            m.Command = parts[0];
            m.Params.AddRange(parts.Skip(1));
        }
        // "PING :tmi.twitch.tv" chega sem prefixo e com o texto logo depois do comando
        if (m.Command.Length == 0 && line.StartsWith("PING", StringComparison.Ordinal))
            m.Command = "PING";
        return m;
    }

    static string Unescape(string v)
    {
        if (v.IndexOf('\\') < 0) return v;
        var sb = new StringBuilder(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] != '\\' || i == v.Length - 1) { sb.Append(v[i]); continue; }
            var c = v[++i];
            sb.Append(c switch { 's' => ' ', ':' => ';', 'r' => '\r', 'n' => '\n', _ => c });
        }
        return sb.ToString();
    }
}

/// <summary>Fila de alertas mostrada pela fonte de navegador "NTOobs Alertas" no OBS.</summary>
public sealed class AlertsHub
{
    public static readonly (string Id, string Name)[] Templates =
    {
        ("classico", "Clássico"),
        ("neon", "Neon"),
        ("minimal", "Minimalista"),
        ("banner", "Faixa"),
        ("pop", "Pop"),
        ("conquista", "Conquista")
    };

    public static readonly (string Kind, string Name, string Title, string Detail)[] Kinds =
    {
        ("follow", "Follow", "Fulano começou a seguir!", ""),
        ("sub", "Sub", "Fulano se inscreveu!", "3 meses de inscrição"),
        ("raid", "Raid", "Raid de Fulano!", "25 pessoas chegando"),
        ("bits", "Bits", "Fulano mandou 100 bits!", "valeu demais!")
    };

    static readonly string[] VideoExt = { ".mp4", ".webm" };

    readonly object _lock = new();
    readonly List<TwitchAlert> _items = new();
    long _next = 1;

    /// <summary>Som tocado quando o tipo de alerta não tem som próprio (vazio = sem som).</summary>
    public string SoundFile { get; set; } = "";

    /// <summary>Tipo de letra dos alertas (o mesmo dos textos da live).</summary>
    public string Font { get; set; } = "Segoe UI";

    public string Template { get; set; } = "classico";
    public string Position { get; set; } = "top";
    public int Duration { get; set; } = 6;

    /// <summary>Imagem, GIF ou vídeo de cada tipo (follow, sub, raid, bits).</summary>
    public IReadOnlyDictionary<string, string> Media { get; set; } = new Dictionary<string, string>();

    /// <summary>Som de cada tipo.</summary>
    public IReadOnlyDictionary<string, string> Sounds { get; set; } = new Dictionary<string, string>();

    public static bool IsVideo(string file) => VideoExt.Contains(Path.GetExtension(file).ToLowerInvariant());

    public void Push(string kind, string title, string detail)
    {
        lock (_lock)
        {
            _items.Add(new TwitchAlert(_next++, kind, title, detail));
            if (_items.Count > 50) _items.RemoveAt(0);
        }
    }

    string? SoundFor(string kind)
    {
        if (Sounds.TryGetValue(kind, out var s) && File.Exists(s)) return s;
        return File.Exists(SoundFile) ? SoundFile : null;
    }

    object Config()
    {
        var media = new Dictionary<string, object?>();
        var sound = new Dictionary<string, bool>();
        foreach (var k in Kinds.Select(k => k.Kind).Append("test"))
        {
            var key = k == "test" ? "follow" : k;
            if (Media.TryGetValue(key, out var file) && File.Exists(file))
                media[k] = new { type = IsVideo(file) ? "video" : "img", v = File.GetLastWriteTimeUtc(file).Ticks ^ file.GetHashCode() };
            else
                media[k] = null;
            sound[k] = SoundFor(key) != null;
        }
        return new { template = Template, position = Position, duration = Math.Clamp(Duration, 2, 30), font = Font, media, sound };
    }

    /// <summary>Responde às páginas /alerts. true = tratado.</summary>
    public bool Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "";
        var kind = ctx.Request.QueryString["kind"] ?? "";
        if (kind == "test") kind = "follow";

        switch (path)
        {
            case "/alerts":
                MusicServer.Write(ctx, "text/html; charset=utf-8", AlertsHtml);
                return true;

            case "/alerts/poll":
            {
                _ = long.TryParse(ctx.Request.QueryString["after"], out var after);
                List<TwitchAlert> items;
                long last;
                lock (_lock)
                {
                    last = _next - 1;
                    items = after < 0 ? new() : _items.Where(a => a.Id > after).ToList();
                }
                var json = JsonSerializer.Serialize(new
                {
                    last,
                    cfg = Config(),
                    items = items.Select(a => new { id = a.Id, kind = a.Kind, title = a.Title, detail = a.Detail })
                });
                MusicServer.Write(ctx, "application/json; charset=utf-8", json);
                return true;
            }

            case "/alerts/sound":
            {
                var file = SoundFor(kind);
                return file != null && SendFile(ctx, file);
            }

            case "/alerts/media":
                return Media.TryGetValue(kind, out var media) && File.Exists(media) && SendFile(ctx, media);
        }
        return false;
    }

    static bool SendFile(HttpListenerContext ctx, string file)
    {
        var bytes = File.ReadAllBytes(file);
        ctx.Response.ContentType = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
        return true;
    }

    const string AlertsHtml = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>NTOobs Alertas</title>
<style>
html,body{margin:0;height:100%;background:transparent;overflow:hidden;font-family:"Segoe UI",Arial,sans-serif}
body.preview{background:#202027 repeating-conic-gradient(#2a2a33 0 25%,#202027 0 50%) 0 0/40px 40px}
#stage{position:absolute;inset:0;display:flex;justify-content:center;align-items:flex-start;padding:6vh 4vw;box-sizing:border-box}
.p-center #stage{align-items:center}
.p-bottom #stage{align-items:flex-end}
#box{--c:#F5A524;opacity:0;color:#fff;text-align:center;box-sizing:border-box}
#media img,#media video{max-width:420px;max-height:300px;display:block;margin:0 auto 10px}
#kind{display:none}
#hint{position:absolute;left:12px;bottom:10px;color:#aaa;font-size:14px;display:none}
body.preview #hint{display:block}

/* Clássico */
.t-classico #box{min-width:520px;max-width:80%;padding:26px 40px;border-radius:22px;background:rgba(12,12,16,.88);
  border:3px solid var(--c);box-shadow:0 12px 40px rgba(0,0,0,.5);transform:translateY(-30px) scale(.9);
  transition:all .45s cubic-bezier(.2,1.4,.4,1)}
.t-classico #box.show{opacity:1;transform:none}
.t-classico #icon{font-size:56px;line-height:1}
.t-classico #title{font-size:40px;font-weight:800;margin-top:8px;text-shadow:0 2px 6px #000}
.t-classico #detail{font-size:24px;opacity:.85;margin-top:6px}
.t-classico #box.has-media #icon{display:none}

/* Neon */
.t-neon #box{padding:10px 30px}
.t-neon #icon{display:none}
.t-neon #title{font-size:58px;font-weight:900;color:#fff;
  text-shadow:0 0 6px #fff,0 0 18px var(--c),0 0 40px var(--c),0 0 80px var(--c)}
.t-neon #detail{font-size:26px;color:var(--c);text-shadow:0 0 10px var(--c);margin-top:6px}
.t-neon #box.show{animation:neonIn 1s forwards}
.t-neon #media img,.t-neon #media video{filter:drop-shadow(0 0 18px var(--c))}

/* Minimalista */
.t-minimal #stage{justify-content:flex-start}
.t-minimal #box{display:flex;align-items:center;gap:14px;text-align:left;background:#fff;color:#111;border-radius:999px;
  padding:12px 28px 12px 14px;border-left:8px solid var(--c);box-shadow:0 6px 24px rgba(0,0,0,.35);
  transform:translateX(-120%);transition:transform .5s cubic-bezier(.2,1,.3,1),opacity .3s}
.t-minimal #box.show{opacity:1;transform:none}
.t-minimal #icon{font-size:30px}
.t-minimal #title{font-size:26px;font-weight:700}
.t-minimal #detail{font-size:16px;color:#555}
.t-minimal #media img,.t-minimal #media video{max-height:64px;max-width:64px;margin:0;border-radius:50%;object-fit:cover}
.t-minimal #box.has-media #icon{display:none}

/* Faixa */
.t-banner #stage{padding:0}
.t-banner #box{width:100%;padding:22px 40px;background:linear-gradient(90deg,var(--c),rgba(0,0,0,.88) 75%);
  display:flex;align-items:center;justify-content:center;gap:24px;transform:scaleX(0);transform-origin:left;
  transition:transform .5s ease,opacity .2s}
.t-banner #box.show{opacity:1;transform:scaleX(1)}
.t-banner #icon{font-size:44px}
.t-banner #txt{text-align:left}
.t-banner #kind{display:block;font-size:16px;letter-spacing:.3em;opacity:.85}
.t-banner #title{font-size:38px;font-weight:800}
.t-banner #detail{font-size:20px;opacity:.9}
.t-banner #media img,.t-banner #media video{max-height:110px;margin:0}
.t-banner #box.has-media #icon{display:none}

/* Pop */
.t-pop #icon{font-size:90px;line-height:1}
.t-pop #title{font-size:64px;font-weight:900;background:linear-gradient(45deg,var(--c),#fff,var(--c));
  -webkit-background-clip:text;background-clip:text;color:transparent;filter:drop-shadow(0 4px 6px rgba(0,0,0,.7))}
.t-pop #detail{font-size:28px;font-weight:700;text-shadow:0 2px 6px #000}
.t-pop #box.show{animation:popIn .7s cubic-bezier(.2,1.6,.4,1) forwards}
.t-pop #box.show #icon,.t-pop #box.show #media{animation:wiggle 1s ease-in-out infinite}
.t-pop #box.has-media #icon{display:none}

/* Conquista */
.t-conquista #box{display:flex;align-items:center;gap:18px;text-align:left;background:linear-gradient(#2d2d35,#17171b);
  border-radius:60px;padding:12px 40px 12px 12px;border:2px solid #555;box-shadow:0 10px 30px rgba(0,0,0,.6);
  max-width:110px;overflow:hidden;white-space:nowrap;transition:max-width .8s ease .25s,opacity .25s}
.t-conquista #box.show{opacity:1;max-width:1400px}
.t-conquista #icon{flex:none;width:84px;height:84px;border-radius:50%;background:var(--c);display:flex;
  align-items:center;justify-content:center;font-size:44px;box-shadow:inset 0 0 0 4px rgba(255,255,255,.25)}
.t-conquista #kind{display:block;font-size:15px;letter-spacing:.15em;color:#bbb}
.t-conquista #title{font-size:30px;font-weight:700}
.t-conquista #detail{font-size:18px;color:#ccc}
.t-conquista #media img,.t-conquista #media video{width:84px;height:84px;max-width:none;object-fit:cover;border-radius:50%;margin:0;flex:none}
.t-conquista #box.has-media #icon{display:none}

/* saída */
#box.show.hide{opacity:0;transition:opacity .6s}
.t-neon #box.show.hide{animation:fadeOut .6s forwards}
.t-pop #box.show.hide{animation:popOut .5s forwards}

@keyframes neonIn{0%{opacity:0}10%{opacity:1}15%{opacity:.2}25%{opacity:1}30%{opacity:.4}40%,100%{opacity:1}}
@keyframes fadeOut{from{opacity:1}to{opacity:0}}
@keyframes popIn{0%{opacity:0;transform:scale(.2) rotate(-8deg)}100%{opacity:1;transform:scale(1) rotate(0)}}
@keyframes popOut{from{opacity:1;transform:scale(1)}to{opacity:0;transform:scale(1.4)}}
@keyframes wiggle{0%,100%{transform:rotate(-8deg)}50%{transform:rotate(8deg)}}
</style>
</head>
<body class="t-classico p-top">
<div id="stage"><div id="box"><div id="media"></div><div id="icon"></div>
<div id="txt"><div id="kind"></div><div id="title"></div><div id="detail"></div></div></div></div>
<div id="hint">Prévia dos alertas do NTOobs. No OBS, o fundo fica transparente.</div>
<script>
const preview=new URLSearchParams(location.search).has('preview');
const $=id=>document.getElementById(id);
const box=$('box'),media=$('media'),icon=$('icon'),kindEl=$('kind'),title=$('title'),detail=$('detail');
const COLORS={follow:'#9146FF',sub:'#F5A524',raid:'#34D399',bits:'#38BDF8',test:'#F5A524'};
const ICONS={follow:'💜',sub:'⭐',raid:'🚀',bits:'💎',test:'🔔'};
const LABELS={follow:'NOVO SEGUIDOR',sub:'NOVO SUB',raid:'RAID',bits:'BITS',test:'ALERTA DE TESTE'};
const SAMPLES=[['follow','Fulano começou a seguir!',''],['sub','Fulano se inscreveu!','3 meses de inscrição'],
  ['raid','Raid de Fulano!','25 pessoas chegando'],['bits','Fulano mandou 100 bits!','valeu demais!']];
let last=-1,queue=[],busy=false,cfg=null,cfgKey='',sample=0;

function applyCfg(c){
  const key=JSON.stringify(c);if(key===cfgKey)return;cfgKey=key;cfg=c;
  document.body.className='t-'+c.template+' p-'+c.position+(preview?' preview':'');
  box.style.fontFamily='"'+String(c.font||'Segoe UI').replace(/"/g,'')+'","Segoe UI",Arial,sans-serif';
}
function finish(){
  box.classList.add('hide');
  setTimeout(()=>{box.classList.remove('show','hide');media.innerHTML='';busy=false;show();},800);
}
function show(){
  if(busy||!queue.length||!cfg)return;
  busy=true;const a=queue.shift();
  box.style.setProperty('--c',COLORS[a.kind]||COLORS.test);
  icon.textContent=ICONS[a.kind]||ICONS.test;kindEl.textContent=LABELS[a.kind]||'ALERTA';
  title.textContent=a.title;detail.textContent=a.detail||'';detail.style.display=a.detail?'':'none';
  media.innerHTML='';box.classList.remove('has-media');
  let dur=(cfg.duration||6)*1000;const m=cfg.media[a.kind];
  let started=false;const start=()=>{if(started)return;started=true;void box.offsetWidth;box.classList.add('show');setTimeout(finish,dur);};
  if(m){
    const url='/alerts/media?kind='+a.kind+'&v='+m.v;box.classList.add('has-media');
    if(m.type==='video'){
      const v=document.createElement('video');v.src=url;v.autoplay=true;v.playsInline=true;
      v.onloadedmetadata=()=>{if(isFinite(v.duration))dur=Math.max(dur,Math.min(v.duration,15)*1000);start();};
      v.onerror=start;media.appendChild(v);setTimeout(start,1500);
    }else{
      const img=new Image();img.src=url;img.onload=start;img.onerror=start;media.appendChild(img);setTimeout(start,1500);
    }
  }else start();
  if(cfg.sound[a.kind]){try{new Audio('/alerts/sound?kind='+a.kind+'&t='+Date.now()).play();}catch(e){}}
}
async function poll(){
  try{
    const r=await fetch('/alerts/poll?after='+last);const d=await r.json();
    applyCfg(d.cfg);
    if(last>=0){for(const a of d.items)queue.push(a);}
    last=d.last;show();
  }catch(e){}
  setTimeout(poll,1000);
}
if(preview){setInterval(()=>{if(!busy&&!queue.length){const s=SAMPLES[sample++%SAMPLES.length];queue.push({kind:s[0],title:s[1],detail:s[2]});show();}},1500);}
poll();
</script>
</body>
</html>
""";
}

/// <summary>Página que recebe o login da Twitch (o token volta no endereço, depois do #).</summary>
public static class TwitchLoginPage
{
    public const string Html = """
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>NTOobs - Twitch</title>
<style>body{font-family:"Segoe UI",Arial;background:#0A0A0B;color:#F2F2F3;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
div{max-width:460px;text-align:center}h1{color:#9146FF}</style></head>
<body><div><h1>NTOobs</h1><p id="m">Conectando...</p></div>
<script>
const h=new URLSearchParams(location.hash.slice(1)),q=new URLSearchParams(location.search);
const token=h.get('access_token'),state=h.get('state')||q.get('state'),err=q.get('error_description')||q.get('error');
const m=document.getElementById('m');
if(token){
  fetch('/twitch/token',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},
    body:'token='+encodeURIComponent(token)+'&state='+encodeURIComponent(state||'')})
  .then(r=>r.text()).then(t=>{m.textContent=t;history.replaceState(null,'',location.pathname);})
  .catch(()=>{m.textContent='Não consegui falar com o NTOobs. Ele está aberto?';});
}else{m.textContent='Login cancelado ou recusado'+(err?': '+err:'.');}
</script></body></html>
""";
}
