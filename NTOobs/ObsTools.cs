using System.Text.Json;

namespace NTOobs;

/// <summary>Funções comuns sobre o OBS usadas pelas ferramentas (textos, cenas, tela cheia).</summary>
public static class ObsTools
{
    public static async Task<string> GetCurrentSceneAsync(ObsClient obs)
    {
        var d = await obs.RequestAsync("GetCurrentProgramScene");
        if (d.TryGetProperty("currentProgramSceneName", out var n)) return n.GetString() ?? "";
        return d.TryGetProperty("sceneName", out var s) ? s.GetString() ?? "" : "";
    }

    public static async Task<List<string>> GetSceneNamesAsync(ObsClient obs)
    {
        var (scenes, _) = await MusicObs.GetScenesAsync(obs);
        return scenes;
    }

    public static async Task<bool> SceneExistsAsync(ObsClient obs, string scene)
    {
        if (string.IsNullOrWhiteSpace(scene)) return false;
        var scenes = await GetSceneNamesAsync(obs);
        return scenes.Contains(scene);
    }

    /// <summary>Todas as fontes (inputs) do OBS, em ordem alfabética.</summary>
    public static async Task<List<string>> GetInputNamesAsync(ObsClient obs)
    {
        var d = await obs.RequestAsync("GetInputList");
        return d.GetProperty("inputs").EnumerateArray()
            .Select(i => i.GetProperty("inputName").GetString() ?? "")
            .Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<(int Width, int Height)> GetCanvasAsync(ObsClient obs)
    {
        var d = await obs.RequestAsync("GetVideoSettings");
        return (d.GetProperty("baseWidth").GetInt32(), d.GetProperty("baseHeight").GetInt32());
    }

    static string? _textKind;

    /// <summary>Tipo da fonte de texto disponível (o nome muda entre versões do OBS).</summary>
    public static async Task<string> GetTextKindAsync(ObsClient obs)
    {
        if (_textKind != null) return _textKind;
        var d = await obs.RequestAsync("GetInputKindList", new { unversioned = false });
        var kinds = d.GetProperty("inputKinds").EnumerateArray().Select(k => k.GetString() ?? "").ToList();
        _textKind =
            kinds.Where(k => k.StartsWith("text_gdiplus")).OrderByDescending(k => k).FirstOrDefault()
            ?? kinds.Where(k => k.StartsWith("text_ft2")).OrderByDescending(k => k).FirstOrDefault()
            ?? "text_gdiplus_v3";
        return _textKind;
    }

    /// <summary>Reinicia o cache (outro OBS pode ter outra versão).</summary>
    public static void ResetCache() => _textKind = null;

    /// <summary>Configurações de fonte, cor e contorno no formato da fonte de texto do OBS.</summary>
    public static Dictionary<string, object> StyleSettings(string kind, TextStyle st)
    {
        var d = new Dictionary<string, object>
        {
            ["font"] = new Dictionary<string, object>
            {
                ["face"] = st.Font,
                ["size"] = Math.Clamp(st.Size, 8, 1000),
                ["style"] = st.Bold ? "Bold" : "Regular",
                ["flags"] = st.Bold ? 1 : 0
            }
        };
        var color = TextStyle.ToObsColor(st.Color);
        if (kind.StartsWith("text_ft2"))
        {
            d["color1"] = color;
            d["color2"] = color;
            d["outline"] = st.Outline;
            d["drop_shadow"] = st.Outline;
        }
        else
        {
            d["color"] = color;
            d["opacity"] = 100;
            d["outline"] = st.Outline;
            d["outline_size"] = Math.Clamp(st.OutlineSize, 1, 20);
            d["outline_color"] = TextStyle.ToObsColor(st.OutlineColor);
            d["outline_opacity"] = 100;
        }
        return d;
    }

    /// <summary>Cria (se não existir) uma fonte de texto na cena. Se center, coloca no meio da tela.</summary>
    public static async Task EnsureTextAsync(ObsClient obs, string scene, string name, string text, bool center, TextStyle style)
    {
        if (await MusicObs.InputExistsAsync(obs, name))
        {
            await ApplyStyleAsync(obs, name, style, text);
            if (!await HasItemAsync(obs, scene, name))
            {
                var item = await obs.RequestAsync("CreateSceneItem", new { sceneName = scene, sourceName = name, sceneItemEnabled = true });
                if (center) await CenterAsync(obs, scene, item.GetProperty("sceneItemId").GetInt32());
            }
            return;
        }

        var kind = await GetTextKindAsync(obs);
        var settings = StyleSettings(kind, style);
        settings["text"] = text;
        var created = await obs.RequestAsync("CreateInput", new
        {
            sceneName = scene,
            inputName = name,
            inputKind = kind,
            inputSettings = settings,
            sceneItemEnabled = true
        });
        if (center) await CenterAsync(obs, scene, created.GetProperty("sceneItemId").GetInt32());
    }

    /// <summary>Aplica fonte, tamanho, cor e contorno num texto que já existe (e troca o texto, se informado).</summary>
    public static async Task ApplyStyleAsync(ObsClient obs, string name, TextStyle style, string? text = null)
    {
        var info = await obs.RequestAsync("GetInputSettings", new { inputName = name });
        var kind = GetString(info, "inputKind");
        if (kind.Length == 0) kind = await GetTextKindAsync(obs);
        var settings = StyleSettings(kind, style);
        if (text != null) settings["text"] = text;
        await obs.RequestAsync("SetInputSettings", new { inputName = name, inputSettings = settings, overlay = true });
    }

    public static Task SetTextAsync(ObsClient obs, string name, string text) =>
        obs.RequestAsync("SetInputSettings", new { inputName = name, inputSettings = new { text }, overlay = true });

    public static async Task<bool> HasItemAsync(ObsClient obs, string scene, string name)
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

    public static async Task CenterAsync(ObsClient obs, string scene, int sceneItemId)
    {
        var (w, h) = await GetCanvasAsync(obs);
        await obs.RequestAsync("SetSceneItemTransform", new
        {
            sceneName = scene,
            sceneItemId,
            sceneItemTransform = new { positionX = w / 2.0, positionY = h / 2.0, alignment = 0 }
        });
    }

    /// <summary>Estica o item para a tela inteira, mantendo a proporção.</summary>
    public static async Task FullscreenAsync(ObsClient obs, string scene, int sceneItemId)
    {
        var (w, h) = await GetCanvasAsync(obs);
        await obs.RequestAsync("SetSceneItemTransform", new
        {
            sceneName = scene,
            sceneItemId,
            sceneItemTransform = new
            {
                positionX = 0.0,
                positionY = 0.0,
                alignment = 5, // canto superior esquerdo
                boundsType = "OBS_BOUNDS_SCALE_INNER",
                boundsAlignment = 0,
                boundsWidth = (double)w,
                boundsHeight = (double)h
            }
        });
    }

    /// <summary>"01:23:45.678" vira "01:23:45".</summary>
    public static string ShortTimecode(string? tc)
    {
        if (string.IsNullOrEmpty(tc)) return "";
        var dot = tc.IndexOf('.');
        return dot > 0 ? tc[..dot] : tc;
    }

    public static bool GetBool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static double GetDouble(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

    public static string GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
