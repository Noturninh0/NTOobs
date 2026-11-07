using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NTOobs;

/// <summary>Cliente mínimo do obs-websocket v5 (embutido no OBS 28+), sem dependências externas.</summary>
public sealed class ObsClient : IDisposable
{
    ClientWebSocket? _ws;
    CancellationTokenSource? _cts;
    TaskCompletionSource<bool> _identified = new();
    string _password = "";
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    /// <summary>Disparado quando uma conexão que estava funcionando cai.</summary>
    public event Action? Disconnected;

    public bool IsConnected =>
        _ws?.State == WebSocketState.Open && _identified.Task.IsCompletedSuccessfully;

    public async Task ConnectAsync(string host, int port, string password)
    {
        await DisconnectAsync();

        _password = password;
        _identified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cts = new CancellationTokenSource();
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("obswebsocket.json"); // exigido pelo obs-websocket v5
        _ws = ws;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) host = "127.0.0.1";

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(5000);
            await ws.ConnectAsync(new Uri($"ws://{host}:{port}"), connectCts.Token);
            _ = Task.Run(() => ReceiveLoopAsync(ws, _cts.Token));

            var finished = await Task.WhenAny(_identified.Task, Task.Delay(5000));
            if (finished != _identified.Task)
                throw new TimeoutException("O OBS não respondeu à autenticação.");
            await _identified.Task;
        }
        catch (Exception ex)
        {
            await DisconnectAsync();
            if (ex is WebSocketException || ex is InvalidOperationException)
                throw new InvalidOperationException(
                    "Não foi possível conectar (" + (ex.InnerException?.Message ?? ex.Message) +
                    "). Verifique se o OBS está aberto, o servidor WebSocket ligado, a porta e a senha.", ex);
            if (ex is OperationCanceledException)
                throw new TimeoutException(
                    $"O OBS não respondeu em {host}:{port}. Confira se o servidor WebSocket está ligado e se a porta é essa.");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var ws = _ws;
        var cts = _cts;
        _ws = null;
        _cts = null;

        try { cts?.Cancel(); } catch { }
        try
        {
            if (ws is { State: WebSocketState.Open })
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch { }
        ws?.Dispose();
        cts?.Dispose();
        FailAll(new OperationCanceledException("Desconectado."));
    }

    public async Task<JsonElement> RequestAsync(string requestType, object? requestData = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Não conectado ao OBS.");

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await SendAsync(new
            {
                op = 6,
                d = new { requestType, requestId = id, requestData = requestData ?? new { } }
            });

            var finished = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            if (finished != tcs.Task)
                throw new TimeoutException($"O OBS não respondeu a {requestType}.");

            var d = await tcs.Task;
            var status = d.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
            {
                var comment = status.TryGetProperty("comment", out var c) ? c.GetString() : "erro desconhecido";
                throw new InvalidOperationException($"{requestType}: {comment}");
            }
            return d.TryGetProperty("responseData", out var rd) ? rd : default;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    async Task SendAsync(object message)
    {
        var ws = _ws ?? throw new InvalidOperationException("Não conectado ao OBS.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _sendLock.WaitAsync();
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException("O OBS encerrou a conexão (senha incorreta?).");
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                Handle(ms.ToArray());
            }
        }
        catch (Exception ex)
        {
            // só trata se esta ainda é a conexão atual (não foi uma desconexão pedida por nós)
            if (ReferenceEquals(_ws, ws))
            {
                var wasUp = _identified.Task.IsCompletedSuccessfully;
                _ws = null;
                FailAll(ex);
                if (wasUp) Disconnected?.Invoke();
            }
        }
    }

    void Handle(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var op = root.GetProperty("op").GetInt32();
        var d = root.GetProperty("d");

        switch (op)
        {
            case 0: // Hello
                object identify;
                if (d.TryGetProperty("authentication", out var auth))
                {
                    var challenge = auth.GetProperty("challenge").GetString()!;
                    var salt = auth.GetProperty("salt").GetString()!;
                    identify = new
                    {
                        op = 1,
                        d = new { rpcVersion = 1, authentication = BuildAuth(salt, challenge), eventSubscriptions = 0 }
                    };
                }
                else
                {
                    identify = new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 0 } };
                }
                _ = Task.Run(async () =>
                {
                    try { await SendAsync(identify); }
                    catch (Exception e) { _identified.TrySetException(e); }
                });
                break;

            case 2: // Identified
                _identified.TrySetResult(true);
                break;

            case 7: // RequestResponse
                var id = d.GetProperty("requestId").GetString();
                if (id != null && _pending.TryRemove(id, out var tcs))
                    tcs.TrySetResult(d.Clone());
                break;
        }
    }

    string BuildAuth(string salt, string challenge)
    {
        static string Sha256B64(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
        var secret = Sha256B64(_password + salt);
        return Sha256B64(secret + challenge);
    }

    void FailAll(Exception ex)
    {
        _identified.TrySetException(ex);
        foreach (var kv in _pending)
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetException(ex);
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
