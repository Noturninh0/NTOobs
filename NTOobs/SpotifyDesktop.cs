using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NTOobs;

/// <summary>
/// Controla o aplicativo Spotify do computador (músicas completas, sem o limite de 30 segundos
/// do player embutido). O estado é lido pelo título da janela do Spotify e o play/pause é enviado
/// só para a janela do Spotify, sem mexer em outros players.
/// </summary>
public static class SpotifyDesktop
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    const uint WM_APPCOMMAND = 0x0319;
    const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

    static IntPtr FindMainWindow(out string title)
    {
        var procs = Process.GetProcessesByName("Spotify");
        var pids = procs.Select(p => (uint)p.Id).ToHashSet();
        foreach (var p in procs) p.Dispose();

        IntPtr found = IntPtr.Zero;
        string foundTitle = "";

        if (pids.Count > 0)
        {
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (!pids.Contains(pid)) return true;

                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, cls.Capacity);
                if (!cls.ToString().StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) return true;

                var text = new StringBuilder(512);
                GetWindowText(hwnd, text, text.Capacity);
                if (text.Length == 0) return true;

                found = hwnd;
                foundTitle = text.ToString();
                return false;
            }, IntPtr.Zero);
        }

        title = foundTitle;
        return found;
    }

    /// <summary>Running: o Spotify está aberto. Playing: está tocando (título "Artista - Música").</summary>
    public static (bool Running, bool Playing, string Track) GetState()
    {
        var hwnd = FindMainWindow(out var title);
        if (hwnd == IntPtr.Zero) return (false, false, "");

        // parado ou em pausa, o título volta a ser "Spotify Free", "Spotify Premium" ou "Spotify"
        bool playing = !title.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase);
        return (true, playing, playing ? title : "");
    }

    public static bool TogglePlayPause()
    {
        var hwnd = FindMainWindow(out _);
        if (hwnd == IntPtr.Zero) return false;
        SendMessage(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)(APPCOMMAND_MEDIA_PLAY_PAUSE << 16));
        return true;
    }

    /// <summary>Abre a playlist/álbum/música no aplicativo (abre o Spotify se estiver fechado).</summary>
    public static void Open(MusicLink link)
    {
        var uri = $"spotify:{link.Type}:{link.Id}";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}
