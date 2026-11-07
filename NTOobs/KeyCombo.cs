namespace NTOobs;

public readonly record struct ObsModifiers(bool Shift, bool Control, bool Alt, bool Command);

/// <summary>Converte textos como "Ctrl+Alt+Shift+6" no formato de teclas do OBS (OBS_KEY_6 + modificadores).</summary>
public static class ObsKeys
{
    static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "OBS_KEY_SPACE",
        ["enter"] = "OBS_KEY_RETURN",
        ["return"] = "OBS_KEY_RETURN",
        ["esc"] = "OBS_KEY_ESCAPE",
        ["escape"] = "OBS_KEY_ESCAPE",
        ["tab"] = "OBS_KEY_TAB",
        ["backspace"] = "OBS_KEY_BACKSPACE",
        ["insert"] = "OBS_KEY_INSERT",
        ["delete"] = "OBS_KEY_DELETE",
        ["del"] = "OBS_KEY_DELETE",
        ["home"] = "OBS_KEY_HOME",
        ["end"] = "OBS_KEY_END",
        ["pageup"] = "OBS_KEY_PAGEUP",
        ["pagedown"] = "OBS_KEY_PAGEDOWN",
        ["up"] = "OBS_KEY_UP",
        ["down"] = "OBS_KEY_DOWN",
        ["left"] = "OBS_KEY_LEFT",
        ["right"] = "OBS_KEY_RIGHT",
        ["pause"] = "OBS_KEY_PAUSE",
        ["printscreen"] = "OBS_KEY_PRINT",
    };

    public static (string KeyId, ObsModifiers Mods) Parse(string combo)
    {
        bool shift = false, ctrl = false, alt = false, cmd = false;
        string? key = null;

        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "win":
                case "windows":
                case "cmd":
                case "command":
                    cmd = true;
                    break;
                default:
                    if (key != null)
                        throw new FormatException("Use apenas uma tecla principal na combinação (ex.: Ctrl+Alt+Shift+6).");
                    key = part;
                    break;
            }
        }

        if (key == null)
            throw new FormatException("Informe a tecla principal da combinação (ex.: Ctrl+Alt+Shift+6).");

        return (ToKeyId(key), new ObsModifiers(shift, ctrl, alt, cmd));
    }

    static string ToKeyId(string key)
    {
        if (key.Length == 1 && key[0] < 128 && char.IsLetterOrDigit(key[0]))
            return "OBS_KEY_" + char.ToUpperInvariant(key[0]);

        if (Named.TryGetValue(key, out var named))
            return named;

        if ((key[0] == 'F' || key[0] == 'f') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return "OBS_KEY_F" + f;

        if (key.Length == 4 && key.StartsWith("num", StringComparison.OrdinalIgnoreCase) && char.IsDigit(key[3]))
            return "OBS_KEY_NUM" + key[3];

        throw new FormatException($"Tecla não reconhecida: {key}");
    }
}
