using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;

namespace NTOobs;

public sealed record PhoneButton(string Id, string Label, string Sub, string Group);

/// <summary>
/// Controle pelo celular: um servidor HTTP simples na rede local (Wi-Fi), protegido por um código.
/// Usa TcpListener para não precisar de permissão de administrador no Windows.
/// </summary>
public sealed class PhoneServer : IDisposable
{
    TcpListener? _listener;
    CancellationTokenSource? _cts;

    public int Port { get; private set; }
    public string Key { get; set; } = "";
    public bool Running => _listener != null;

    /// <summary>Lista de botões (chamado a cada atualização da página).</summary>
    public Func<Task<List<PhoneButton>>>? GetButtons { get; set; }

    /// <summary>Executa o botão. Retorna a mensagem mostrada no celular.</summary>
    public Func<string, Task<string>>? Run { get; set; }

    public void Start(int port)
    {
        Stop();
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        _listener = listener;
        Port = port;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts = null;
    }

    public void Dispose() => Stop();

    public static string NewKey() => Random.Shared.Next(100000, 1000000).ToString();

    /// <summary>Endereços IPv4 desta máquina na rede local.</summary>
    public static List<string> LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = ni.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue; // ignora adaptadores virtuais sem rede
                foreach (var a in props.UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(a.Address.ToString());
            }
        }
        catch { }
        return list.Distinct().ToList();
    }

    async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                var stream = client.GetStream();

                // lê só o cabeçalho (as requisições do celular são GET, sem corpo)
                var buffer = new byte[8192];
                int total = 0;
                while (total < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
                    if (n == 0) break;
                    total += n;
                    if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
                }

                var head = Encoding.ASCII.GetString(buffer, 0, total);
                var firstLine = head.Split("\r\n")[0].Split(' ');
                if (firstLine.Length < 2)
                    return;

                var target = firstLine[1];
                var q = target.IndexOf('?');
                var path = q >= 0 ? target[..q] : target;
                var query = HttpUtility.ParseQueryString(q >= 0 ? target[(q + 1)..] : "");

                if (query["k"] != Key || Key.Length == 0)
                {
                    await RespondAsync(stream, 403, "text/html; charset=utf-8",
                        "<meta name=viewport content='width=device-width'><body style='font-family:sans-serif;background:#0A0A0B;color:#eee;padding:24px'>" +
                        "<h2>NTOobs</h2><p>Código inválido. Use o endereço completo mostrado na aba Celular do NTOobs.</p></body>");
                    return;
                }

                switch (path)
                {
                    case "/":
                        await RespondAsync(stream, 200, "text/html; charset=utf-8", PageHtml);
                        break;

                    case "/api/buttons":
                        var buttons = GetButtons != null ? await GetButtons() : new List<PhoneButton>();
                        await RespondAsync(stream, 200, "application/json; charset=utf-8",
                            JsonSerializer.Serialize(buttons.Select(b => new { id = b.Id, label = b.Label, sub = b.Sub, group = b.Group })));
                        break;

                    case "/api/run":
                        var msg = Run != null ? await Run(query["id"] ?? "") : "Indisponível";
                        await RespondAsync(stream, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(new { msg }));
                        break;

                    default:
                        await RespondAsync(stream, 404, "text/plain; charset=utf-8", "Não encontrado");
                        break;
                }
            }
            catch
            {
                // conexão caiu: ignora
            }
        }
    }

    static async Task RespondAsync(Stream stream, int code, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var status = code switch { 200 => "OK", 403 => "Forbidden", _ => "Not Found" };
        var header = $"HTTP/1.1 {code} {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\n" +
                     "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        var h = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(h);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    const string PageHtml = """
<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,user-scalable=no">
<meta name="theme-color" content="#0A0A0B">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-capable" content="yes">
<title>NTOobs</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:#0A0A0B;color:#F2F2F3;font-family:"Segoe UI",Roboto,Arial,sans-serif;padding:14px 12px 80px}
header{display:flex;align-items:center;gap:8px;margin:2px 4px 14px}
header b{font-size:18px}header span{color:#8C8C96;font-size:13px}
h3{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#8C8C96;margin:18px 4px 8px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:10px}
button{background:#141418;border:1px solid #26262c;color:#F2F2F3;border-radius:16px;padding:18px 10px;min-height:86px;
 font-size:15px;font-weight:600;text-align:center;font-family:inherit;transition:transform .08s,background .15s}
button small{display:block;font-weight:400;color:#8C8C96;font-size:12px;margin-top:5px}
button:active{transform:scale(.96);background:#F5A524;color:#111}
button:active small{color:#333}
#toast{position:fixed;left:50%;bottom:20px;transform:translateX(-50%);background:#F5A524;color:#111;padding:10px 18px;
 border-radius:20px;font-weight:600;opacity:0;transition:opacity .2s;max-width:90%;text-align:center}
#empty{color:#8C8C96;padding:20px 4px}
</style>
</head>
<body>
<header><b>NTOobs</b><span>controle da live</span></header>
<div id="list"><div id="empty">Carregando...</div></div>
<div id="toast"></div>
<script>
const k=new URLSearchParams(location.search).get('k');
const list=document.getElementById('list'),toast=document.getElementById('toast');
let sig='';
function esc(s){return String(s).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));}
function say(t){toast.textContent=t;toast.style.opacity=1;clearTimeout(say.t);say.t=setTimeout(()=>toast.style.opacity=0,1800);}
async function load(){
  try{
    const r=await fetch('/api/buttons?k='+k);const b=await r.json();
    const s=JSON.stringify(b);if(s===sig)return;sig=s;
    if(!b.length){list.innerHTML='<div id="empty">Nenhum botão. Crie atalhos, macros ou sons no NTOobs.</div>';return;}
    const groups={};for(const x of b)(groups[x.group]=groups[x.group]||[]).push(x);
    let h='';for(const g in groups){h+='<h3>'+esc(g)+'</h3><div class="grid">';
      for(const x of groups[g])h+='<button data-id="'+esc(x.id)+'">'+esc(x.label)+(x.sub?'<small>'+esc(x.sub)+'</small>':'')+'</button>';
      h+='</div>';}
    list.innerHTML=h;
  }catch(e){list.innerHTML='<div id="empty">Sem conexão com o NTOobs. O app está aberto e no mesmo Wi-Fi?</div>';sig='';}
}
list.addEventListener('click',async e=>{
  const b=e.target.closest('button');if(!b)return;
  if(navigator.vibrate)navigator.vibrate(30);
  try{const r=await fetch('/api/run?k='+k+'&id='+encodeURIComponent(b.dataset.id));const d=await r.json();say(d.msg);sig='';load();}
  catch(err){say('Sem conexão com o NTOobs');}
});
load();setInterval(load,4000);
</script>
</body>
</html>
""";
}
