using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace NTOobs;

public enum ActionType
{
    Scene,
    ToggleMute,
    ToggleRecord,
    ToggleStream,
    Hotkey,
    KeyCombo,
    Backtrack,
    MusicToggle,
    SceneToggle,
    PreviousScene,
    Macro,
    Sound,
    CounterAdd,
    CounterSub,
    CounterReset,
    Marker,
    InstantReplay,
    GoLive
}

/// <summary>Que tipo de lista ajuda a escolher o destino da ação.</summary>
public enum TargetList { None, Scenes, Inputs, Hotkeys, Macros, Sounds, Counters }

public sealed record ActionOption(ActionType Type, string Label, bool NeedsTarget, string TargetHint,
    TargetList List = TargetList.None, string DefaultTarget = "");

public static class Actions
{
    public static readonly ActionOption[] All =
    {
        new(ActionType.Scene, "Trocar de cena", true, "3. Nome da cena:", TargetList.Scenes),
        new(ActionType.SceneToggle, "Alternar cena (aperta de novo e volta)", true, "3. Cena (ex.: Volto já):", TargetList.Scenes),
        new(ActionType.PreviousScene, "Voltar para a cena anterior", false, "3. (esta ação não precisa de destino)"),
        new(ActionType.ToggleMute, "Mutar / desmutar fonte de áudio", true, "3. Nome da fonte de áudio:", TargetList.Inputs),
        new(ActionType.ToggleRecord, "Iniciar / parar gravação", false, "3. (esta ação não precisa de destino)"),
        new(ActionType.ToggleStream, "Iniciar / parar transmissão", false, "3. (esta ação não precisa de destino)"),
        new(ActionType.GoLive, "Começar live (checklist e contagem)", false, "3. Configure na aba Ferramentas."),
        new(ActionType.Marker, "Marcar momento", false, "3. (salva o horário da live para cortes)"),
        new(ActionType.InstantReplay, "Replay instantâneo", false, "3. Configure na aba Ferramentas."),
        new(ActionType.Backtrack, "Salvar Backtrack", true, "3. Combinação que o OBS usa para salvar o backtrack (escolha a sua):", TargetList.None, "Ctrl+Alt+Shift+6"),
        new(ActionType.Hotkey, "Executar um atalho do OBS (lista)", true, "3. Nome interno do atalho:", TargetList.Hotkeys),
        new(ActionType.KeyCombo, "Simular combinação de teclas no OBS", true, "3. Combinação (ex.: Ctrl+Alt+F9):"),
        new(ActionType.MusicToggle, "Pausar / retomar a música live", false, "3. (esta ação não precisa de destino)"),
        new(ActionType.Sound, "Tocar som do soundboard", true, "3. Nome do som:", TargetList.Sounds),
        new(ActionType.Macro, "Executar macro", true, "3. Nome da macro:", TargetList.Macros),
        new(ActionType.CounterAdd, "Contador: somar 1", true, "3. Nome do contador:", TargetList.Counters),
        new(ActionType.CounterSub, "Contador: diminuir 1", true, "3. Nome do contador:", TargetList.Counters),
        new(ActionType.CounterReset, "Contador: zerar", true, "3. Nome do contador:", TargetList.Counters),
    };

    public static ActionOption Get(ActionType t) => All.First(a => a.Type == t);
    public static string Label(ActionType t) => Get(t).Label;

    public static string Describe(ActionType t, string target) =>
        string.IsNullOrEmpty(target) ? Label(t) : $"{Label(t)}: {target}";
}

/// <summary>Um atalho: tecla (opcionalmente restrita a um teclado) -> ação no OBS.</summary>
public sealed class DeckBinding : INotifyPropertyChanged
{
    int _fired;

    public int VKey { get; set; }

    /// <summary>Vazio = qualquer teclado. Senão, identificador do teclado (VID_xxxx&amp;PID_xxxx).</summary>
    public string DeviceId { get; set; } = "";

    public ActionType Action { get; set; }
    public string Target { get; set; } = "";

    [JsonIgnore] public string KeyName => KeyInterop.KeyFromVirtualKey(VKey).ToString();
    [JsonIgnore] public string DeviceLabel => string.IsNullOrEmpty(DeviceId) ? "Qualquer teclado" : DeviceId;
    [JsonIgnore] public string ActionLabel => Actions.Label(Action);

    [JsonIgnore]
    public string Summary =>
        string.IsNullOrEmpty(Target) ? $"{ActionLabel}  ·  {DeviceLabel}" : $"{ActionLabel}: {Target}  ·  {DeviceLabel}";

    /// <summary>Quantas vezes disparou nesta sessão (não é salvo).</summary>
    [JsonIgnore]
    public int Fired
    {
        get => _fired;
        set
        {
            _fired = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Fired)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FiredText)));
        }
    }

    [JsonIgnore]
    public string FiredText => _fired switch
    {
        0 => "ainda não disparou",
        1 => "1 vez",
        _ => $"{_fired} vezes"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Conjunto de teclas (um por jogo, por exemplo).</summary>
public sealed class Profile
{
    public string Name { get; set; } = "Padrão";

    /// <summary>Nome do processo do jogo (sem .exe). Vazio = sem troca automática.</summary>
    public string Exe { get; set; } = "";

    public List<DeckBinding> Bindings { get; set; } = new();

    public override string ToString() => string.IsNullOrEmpty(Exe) ? Name : $"{Name}   ({Exe})";
}

public sealed class MacroStep
{
    public ActionType Action { get; set; }
    public string Target { get; set; } = "";

    /// <summary>Espera depois deste passo, em milissegundos.</summary>
    public int DelayMs { get; set; }

    public override string ToString() =>
        Actions.Describe(Action, Target) + (DelayMs > 0 ? $"   ·  esperar {DelayMs} ms" : "");
}

public sealed class Macro
{
    public string Name { get; set; } = "";
    public List<MacroStep> Steps { get; set; } = new();
    public override string ToString() => $"{Name}   ({Steps.Count} passo{(Steps.Count == 1 ? "" : "s")})";
}

public sealed class SoundItem
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public int Volume { get; set; } = 80;
}

public sealed class CounterItem
{
    public string Name { get; set; } = "";
    public int Value { get; set; }

    /// <summary>Texto mostrado na live. {n} vira o número.</summary>
    public string Format { get; set; } = "{nome}: {n}";

    /// <summary>Fonte de texto do OBS que mostra o contador (vazio = não mostra).</summary>
    public string Source { get; set; } = "";

    public string Render() => Format.Replace("{nome}", Name).Replace("{n}", Value.ToString());
}

/// <summary>Aparência dos textos que o NTOobs cria no OBS (contadores e contagem).</summary>
public sealed class TextStyle
{
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 64;
    public string Color { get; set; } = "#FFFFFF";
    public bool Bold { get; set; } = true;
    public bool Outline { get; set; } = true;
    public string OutlineColor { get; set; } = "#000000";
    public int OutlineSize { get; set; } = 6;

    public TextStyle Scaled(double factor)
    {
        var copy = (TextStyle)MemberwiseClone();
        copy.Size = (int)Math.Round(Size * factor);
        return copy;
    }

    /// <summary>"#rgb", "rrggbb" ou "#rrggbb" vira "#RRGGBB". Inválido = null.</summary>
    public static string? NormalizeHex(string? text)
    {
        var h = (text ?? "").Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length != 6 || !h.All(Uri.IsHexDigit)) return null;
        return "#" + h.ToUpperInvariant();
    }

    /// <summary>Cor no formato do OBS (0xAABBGGRR).</summary>
    public static long ToObsColor(string hex)
    {
        var h = (NormalizeHex(hex) ?? "#FFFFFF")[1..];
        long r = Convert.ToInt32(h[..2], 16), g = Convert.ToInt32(h[2..4], 16), b = Convert.ToInt32(h[4..6], 16);
        return 0xFF000000L | (b << 16) | (g << 8) | r;
    }
}

public sealed class TwitchCommand
{
    public string Command { get; set; } = "";
    public string Response { get; set; } = "";
    public override string ToString() => $"{Command}  →  {Response}";
}

public sealed class Settings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public bool DarkTheme { get; set; } = true;
    public string MusicUrl { get; set; } = "";
    public int MusicVolume { get; set; } = 60;
    public bool MusicLoop { get; set; } = true;
    public bool MusicShuffle { get; set; }
    public bool MusicEnabled { get; set; }

    /// <summary>Só usado para migrar configurações antigas (antes dos perfis).</summary>
    public List<DeckBinding> Bindings { get; set; } = new();

    // perfis
    public List<Profile> Profiles { get; set; } = new();
    public string ActiveProfile { get; set; } = "Padrão";
    public bool AutoProfile { get; set; } = true;

    // macros, soundboard, contadores
    public List<Macro> Macros { get; set; } = new();
    public List<SoundItem> Sounds { get; set; } = new();
    public bool SoundMonitor { get; set; }
    public List<CounterItem> Counters { get; set; } = new();
    public TextStyle TextStyle { get; set; } = new();

    // replay instantâneo
    public string ReplayScene { get; set; } = "NTOobs Replay";
    public int ReplaySeconds { get; set; } = 0; // 0 = duração do replay

    // começar live
    public string LiveStartScene { get; set; } = "";
    public string LiveMainScene { get; set; } = "";
    public int LiveCountdown { get; set; } = 300;
    public bool LiveRecord { get; set; }
    public string LiveMic { get; set; } = "";

    // saúde da live
    public bool HealthBeep { get; set; } = true;

    // controle pelo celular
    public bool PhoneEnabled { get; set; }
    public int PhonePort { get; set; } = 47700;
    public string PhoneKey { get; set; } = "";

    // Twitch
    public bool TwitchEnabled { get; set; }
    public string TwitchChannel { get; set; } = "";
    public string TwitchClientId { get; set; } = "";
    public string TwitchToken { get; set; } = "";
    public string TwitchLogin { get; set; } = "";
    public string TwitchUserId { get; set; } = "";
    public bool AlertFollow { get; set; } = true;
    public bool AlertSub { get; set; } = true;
    public bool AlertRaid { get; set; } = true;
    public bool AlertBits { get; set; } = true;
    public string AlertSound { get; set; } = "";
    public string AlertTemplate { get; set; } = "classico";
    public string AlertPosition { get; set; } = "top";
    public int AlertDuration { get; set; } = 6;
    public Dictionary<string, string> AlertMedia { get; set; } = new();
    public Dictionary<string, string> AlertSounds { get; set; } = new();
    public bool CmdMusic { get; set; } = true;
    public bool CmdMark { get; set; } = true;
    public List<TwitchCommand> TwitchCommands { get; set; } = new();
}

public static class ConfigStore
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NTOobs");
    static readonly string FilePath = Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
        }
        catch
        {
            // arquivo corrompido: começa do zero
        }
        return new Settings();
    }

    public static void Save(Settings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Options));
        }
        catch
        {
            // sem permissão de escrita: ignora
        }
    }
}
