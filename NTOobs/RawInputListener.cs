using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;

namespace NTOobs;

/// <summary>
/// Captura teclas de TODOS os teclados via Raw Input, mesmo com o app em segundo plano,
/// e informa de qual teclado físico cada tecla veio (permite usar um teclado externo só para o OBS).
/// </summary>
public sealed class RawInputListener
{
    const int WM_INPUT = 0x00FF;
    const uint RID_INPUT = 0x10000003;
    const uint RIDI_DEVICENAME = 0x20000007;
    const uint RIDEV_INPUTSINK = 0x00000100;
    const uint RIM_TYPEKEYBOARD = 1;
    const ushort RI_KEY_BREAK = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    readonly Dictionary<IntPtr, string> _deviceIds = new();
    readonly HashSet<(IntPtr, ushort)> _down = new();

    /// <summary>(vkey, deviceId) — disparado uma vez por pressionamento (ignora auto-repeat).</summary>
    public event Action<ushort, string>? KeyPressed;

    /// <summary>(vkey, deviceId) — disparado quando a tecla é solta.</summary>
    public event Action<ushort, string>? KeyReleased;

    public void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);

        var rid = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = hwnd }
        };
        if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException("Falha ao registrar a captura de teclado (Raw Input).");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
        {
            try { ProcessInput(lParam); } catch { /* nunca derruba o loop de mensagens */ }
        }
        return IntPtr.Zero;
    }

    void ProcessInput(IntPtr hRawInput)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size) return;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.Type != RIM_TYPEKEYBOARD) return;

            var kb = Marshal.PtrToStructure<RAWKEYBOARD>(buffer + (int)headerSize);
            if (kb.VKey == 0 || kb.VKey == 0xFF) return; // tecla "fantasma" do Windows

            var key = (header.Device, kb.VKey);
            bool isBreak = (kb.Flags & RI_KEY_BREAK) != 0;

            if (isBreak)
            {
                _down.Remove(key);
                KeyReleased?.Invoke(kb.VKey, GetDeviceId(header.Device));
                return;
            }
            if (!_down.Add(key)) return; // auto-repeat

            KeyPressed?.Invoke(kb.VKey, GetDeviceId(header.Device));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    string GetDeviceId(IntPtr device)
    {
        if (_deviceIds.TryGetValue(device, out var cached)) return cached;

        string id = "";
        uint chars = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars > 0)
        {
            var p = Marshal.AllocHGlobal((int)(chars + 1) * 2);
            try
            {
                if (GetRawInputDeviceInfo(device, RIDI_DEVICENAME, p, ref chars) != uint.MaxValue)
                    id = ToId(Marshal.PtrToStringUni(p) ?? "");
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }
        _deviceIds[device] = id;
        return id;
    }

    static string ToId(string deviceName)
    {
        var m = Regex.Match(deviceName, @"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase);
        if (m.Success) return m.Value.ToUpperInvariant();
        var parts = deviceName.Split('#');
        return parts.Length > 1 ? parts[1] : deviceName;
    }
}
