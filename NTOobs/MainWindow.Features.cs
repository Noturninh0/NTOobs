using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NTOobs;

// Partes comuns das funções novas: inicialização, teclas, saúde da live.
public partial class MainWindow
{
    readonly Soundboard _soundboard = new();
    readonly LiveHealth _health = new();
    readonly InstantReplay _replay = new();
    readonly GoLiveRunner _goLive = new();
    readonly TwitchClient _twitch = new();
    readonly AlertsHub _alerts = new();
    readonly PhoneServer _phone = new();
    DispatcherTimer? _featureTimer;
    bool _healthBusy;

    const string SoundHintDefault =
        "Se o som sair duplicado na live, desmarque o monitoramento (o Áudio do Desktop pode estar capturando o seu fone).";

    void InitFeatures()
    {
        _music.ExtraHandler = HandleExtraHttp;
        InitProfilesUi();
        InitMacrosUi();
        RefreshSounds();
        InitToolsUi();
        InitTextStyleUi();
        InitTwitchUi();
        InitAlertsUi();
        InitThemeUi();
        InitPhone();

        _featureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _featureTimer.Tick += async (_, _) =>
        {
            CheckGameProfile();
            await UpdateHealthAsync();
        };
        _featureTimer.Start();
    }

    void ShutdownFeatures()
    {
        _featureTimer?.Stop();
        _twitch.Dispose();
        _phone.Dispose();
    }

    void OnPageShown(int page)
    {
        switch (page)
        {
            case 1: RefreshProfiles(); break;
            case 5: RefreshMacroList(); break;
            case 6: RefreshSounds(); break;
            case 7: RefreshCounters(); RefreshMarkers(); break;
            case 9: RefreshPhoneUi(); break;
        }
    }

    // ---------- Teclas das outras abas ----------

    void StartCapture(string what, Action<ushort, string> callback)
    {
        _capturing = false;
        _capturingCombo = false;
        _capturingMusic = false;
        _captureCallback = callback;
        Log($"Aperte a tecla para {what} (Esc cancela).");
    }

    void RefreshCaptureLabels()
    {
        BtnMacroKey.Content = "Definir tecla";
        BtnAlertsKey.Content = "Definir tecla";
        UpdateAlertsStateUi();
        TxtSoundHint.Text = SoundHintDefault;
        RefreshMacroKey();
        RefreshSounds();
        RefreshCounters();
    }

    /// <summary>Cria a tecla no perfil ativo (substitui a anterior da mesma ação).</summary>
    void AddBindingFor(ActionType type, string target, ushort vk, string device)
    {
        foreach (var old in _bindings.Where(b => b.Action == type && b.Target == target).ToList())
            _bindings.Remove(old);

        var binding = new DeckBinding { VKey = vk, DeviceId = device, Action = type, Target = target };
        _bindings.Add(binding);
        SaveSettings();
        Log($"Tecla {binding.KeyName} ({binding.DeviceLabel}) -> {Actions.Describe(type, target)}, no perfil {ActiveProfileObj.Name}.");
    }

    string KeysFor(ActionType type, string target) =>
        string.Join(", ", _bindings.Where(b => b.Action == type && b.Target == target).Select(b => b.KeyName));

    /// <summary>Apaga as teclas de uma ação em todos os perfis (quando a macro/som/contador é removido).</summary>
    void RemoveBindingsEverywhere(ActionType type, string target)
    {
        var active = ActiveProfileObj;
        foreach (var p in _settings.Profiles)
            if (!ReferenceEquals(p, active))
                p.Bindings.RemoveAll(b => b.Action == type && b.Target == target);
        foreach (var b in _bindings.Where(b => b.Action == type && b.Target == target).ToList())
            _bindings.Remove(b);
        SaveSettings();
    }

    static string UniqueName(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var name = $"{baseName} {i}";
            if (!set.Contains(name)) return name;
        }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            Log("Copiado.");
        }
        catch (Exception ex)
        {
            Log("Não consegui copiar: " + ex.Message);
        }
    }

    // ---------- Saúde da live ----------

    async Task UpdateHealthAsync()
    {
        BtnSkipCountdown.Visibility = _goLive.CountingDown ? Visibility.Visible : Visibility.Collapsed;
        BtnGoLive.Content = _goLive.CountingDown ? "Contando..." : "Começar live";
        if (_healthBusy) return;

        if (!_obs.IsConnected)
        {
            TxtHealthState.Text = "OBS desconectado";
            LiveIcon.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            TxtHTime.Text = TxtHKbps.Text = TxtHDrop.Text = TxtHCpu.Text = TxtHFps.Text = "--";
            return;
        }

        _healthBusy = true;
        try
        {
            var (snap, alert) = await _health.PollAsync(_obs);
            if (snap.Live)
            {
                TxtHealthState.Text = snap.Reconnecting ? "Reconectando..." : snap.Recording ? "Ao vivo e gravando" : "Ao vivo";
                LiveIcon.SetResourceReference(TextBlock.ForegroundProperty, snap.Reconnecting ? "Danger" : "Ok");
                TxtHTime.Text = snap.Time.Length > 0 ? snap.Time : "--";
                TxtHKbps.Text = snap.Kbps > 0 ? $"{snap.Kbps:0} kb/s" : "--";
                TxtHDrop.Text = $"{snap.DropPct:0.#}%";
                TxtHDrop.SetResourceReference(TextBlock.ForegroundProperty, snap.DropPct > 1 ? "Danger" : "Text");
            }
            else
            {
                TxtHealthState.Text = snap.Recording ? $"Fora do ar (gravando {snap.RecordTime})" : "Fora do ar";
                LiveIcon.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                TxtHTime.Text = TxtHKbps.Text = TxtHDrop.Text = "--";
                TxtHDrop.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            }
            TxtHCpu.Text = $"{snap.Cpu:0}%";
            TxtHFps.Text = $"{snap.Fps:0}";

            if (alert != null)
            {
                TxtHealthAlert.Text = $"{DateTime.Now:HH:mm}  {alert}";
                TxtHealthAlert.Visibility = Visibility.Visible;
                Log("Aviso da live: " + alert);
                if (_settings.HealthBeep) GameWatcher.Beep();
            }
            else if (!snap.Live)
            {
                TxtHealthAlert.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            // OBS ocupado ou desconectando: tenta no próximo ciclo
        }
        finally
        {
            _healthBusy = false;
        }
    }
}
