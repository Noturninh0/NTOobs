using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NTOobs;

/// <summary>Descobre qual programa está na frente (para trocar de perfil sozinho quando o jogo abre).</summary>
public static class GameWatcher
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    static extern bool MessageBeep(uint type);

    /// <summary>Nome do processo da janela em primeiro plano (sem .exe), ou vazio.</summary>
    public static string ForegroundProcess()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Programas abertos com janela, para escolher o jogo do perfil.</summary>
    public static List<(string Process, string Title)> WindowedProcesses()
    {
        var list = new List<(string, string)>();
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                list.Add((p.ProcessName, p.MainWindowTitle));
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }
        return list.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Normalize(string exe)
    {
        exe = exe.Trim();
        if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = exe[..^4];
        return exe;
    }

    /// <summary>Som de aviso do Windows.</summary>
    public static void Beep()
    {
        try { MessageBeep(0x30); } catch { }
    }
}
