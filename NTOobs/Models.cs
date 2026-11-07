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
    Backtrack
}

public sealed record ActionOption(ActionType Type, string Label, bool NeedsTarget, string TargetHint, string DefaultTarget = "");

public static class Actions
{
    public static readonly ActionOption[] All =
    {
        new(ActionType.Scene, "Trocar de cena", true, "3. Nome da cena:"),
        new(ActionType.ToggleMute, "Mutar / desmutar fonte de áudio", true, "3. Nome da fonte de áudio:"),
        new(ActionType.ToggleRecord, "Iniciar / parar gravação", false, "3. (esta ação não precisa de destino)"),
        new(ActionType.ToggleStream, "Iniciar / parar transmissão", false, "3. (esta ação não precisa de destino)"),
        new(ActionType.Backtrack, "Salvar Backtrack", true, "3. Combinação que salva o backtrack no OBS:", "Ctrl+Alt+Shift+6"),
        new(ActionType.Hotkey, "Executar um atalho do OBS (lista)", true, "3. Nome interno do atalho:"),
        new(ActionType.KeyCombo, "Simular combinação de teclas no OBS", true, "3. Combinação (ex.: Ctrl+Alt+F9):"),
    };

    public static ActionOption Get(ActionType t) => All.First(a => a.Type == t);
    public static string Label(ActionType t) => Get(t).Label;
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

public sealed class Settings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public bool DarkTheme { get; set; } = true;
    public List<DeckBinding> Bindings { get; set; } = new();
}

public static class ConfigStore
{
    static readonly string Dir = Path.Combine(
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
