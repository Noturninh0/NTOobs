using System.Windows;

namespace NTOobs;

// Controle pelo celular: botões grandes no navegador do celular (mesmo Wi-Fi).
public partial class MainWindow
{
    void InitPhone()
    {
        if (_settings.PhoneKey.Length == 0)
        {
            _settings.PhoneKey = PhoneServer.NewKey();
            SaveSettings();
        }
        _phone.Key = _settings.PhoneKey;
        _phone.GetButtons = () => Dispatcher.InvokeAsync<List<PhoneButton>>(BuildPhoneButtons).Task;
        _phone.Run = id => Dispatcher.InvokeAsync<Task<string>>(() => RunPhoneAsync(id)).Task.Unwrap();

        ChkPhoneEnabled.IsChecked = _settings.PhoneEnabled;
        if (_settings.PhoneEnabled) StartPhone();
        RefreshPhoneUi();
    }

    void StartPhone()
    {
        try
        {
            _phone.Start(_settings.PhonePort);
            TxtPhoneStatus.Text = "Ativo. Abra o endereço no navegador do celular.";
        }
        catch (Exception ex)
        {
            TxtPhoneStatus.Text = "Não consegui ativar: " + ex.Message;
            Log("Celular: " + ex.Message + " (a porta pode estar em uso por outro programa).");
        }
    }

    void RefreshPhoneUi()
    {
        var addresses = PhoneServer.LocalAddresses();
        TxtPhoneUrl.Text = addresses.Count == 0
            ? "Nenhuma rede local encontrada. O computador está conectado ao Wi-Fi ou cabo?"
            : string.Join(Environment.NewLine, addresses.Select(a => $"http://{a}:{_settings.PhonePort}/?k={_settings.PhoneKey}"));
        if (!_settings.PhoneEnabled) TxtPhoneStatus.Text = "Desativado";
    }

    void ChkPhoneEnabled_Click(object sender, RoutedEventArgs e)
    {
        _settings.PhoneEnabled = ChkPhoneEnabled.IsChecked == true;
        SaveSettings();
        if (_settings.PhoneEnabled) StartPhone();
        else _phone.Stop();
        RefreshPhoneUi();
    }

    void BtnPhoneCopy_Click(object sender, RoutedEventArgs e)
    {
        var first = TxtPhoneUrl.Text.Split(Environment.NewLine)[0];
        CopyText(first);
    }

    void BtnPhoneNewKey_Click(object sender, RoutedEventArgs e)
    {
        _settings.PhoneKey = PhoneServer.NewKey();
        _phone.Key = _settings.PhoneKey;
        SaveSettings();
        RefreshPhoneUi();
        Log("Novo código do celular gerado. Abra o endereço novo no celular.");
    }

    List<PhoneButton> BuildPhoneButtons()
    {
        var list = new List<PhoneButton>
        {
            new("live:go", _goLive.CountingDown ? "Pular contagem" : "Começar live", "", "Live"),
            new("live:mark", "Marcar momento", "", "Live"),
            new("live:replay", "Replay", "instantâneo", "Live"),
            new("act:ToggleRecord|", "Gravação", "iniciar / parar", "Live")
        };
        if (_settings.MusicEnabled)
            list.Add(new("act:MusicToggle|", "Música", "pausar / retomar", "Live"));
        list.Add(new("act:AlertsToggle|", _settings.AlertsOn ? "Desligar alertas" : "Ligar alertas",
            _settings.AlertsOn ? "alertas ligados" : "alertas DESLIGADOS", "Live"));

        var profile = ActiveProfileObj.Name;
        for (int i = 0; i < _bindings.Count; i++)
        {
            var b = _bindings[i];
            if (b.Action == ActionType.MusicToggle) continue;
            bool hasTarget = b.Target.Length > 0;
            list.Add(new($"bind:{i}", hasTarget ? b.Target : b.ActionLabel, hasTarget ? b.ActionLabel : b.KeyName, "Atalhos · " + profile));
        }
        foreach (var m in _settings.Macros)
            list.Add(new($"act:Macro|{m.Name}", m.Name, $"{m.Steps.Count} passo(s)", "Macros"));
        foreach (var s in _settings.Sounds)
            list.Add(new($"act:Sound|{s.Name}", s.Name, "", "Soundboard"));
        foreach (var c in _settings.Counters)
        {
            list.Add(new($"act:CounterAdd|{c.Name}", "+1  " + c.Name, c.Render(), "Contadores"));
            list.Add(new($"act:CounterSub|{c.Name}", "−1  " + c.Name, "", "Contadores"));
        }
        return list;
    }

    async Task<string> RunPhoneAsync(string id)
    {
        try
        {
            switch (id)
            {
                case "live:go":
                    RequireObs();
                    var wasCounting = _goLive.CountingDown;
                    _ = GoLiveAsync(false);
                    return wasCounting ? "Contagem pulada" : "Começando a live...";
                case "live:mark":
                    await AddMarkerAsync("(celular)");
                    return "Momento marcado";
                case "live:replay":
                    RequireObs();
                    _ = RunReplayAsync();
                    return "Replay!";
            }

            if (id.StartsWith("bind:") && int.TryParse(id[5..], out var i) && i >= 0 && i < _bindings.Count)
            {
                var b = _bindings[i];
                if (!await RunActionAsync(b.Action, b.Target)) return "Não executou (veja o Registro)";
                b.Fired++;
                RefreshLists();
                Log($"Celular -> {Actions.Describe(b.Action, b.Target)}");
                return "✔ " + (b.Target.Length > 0 ? b.Target : b.ActionLabel);
            }

            if (id.StartsWith("act:"))
            {
                var body = id[4..];
                var bar = body.IndexOf('|');
                if (bar > 0 && Enum.TryParse<ActionType>(body[..bar], out var type))
                {
                    var target = body[(bar + 1)..];
                    if (!await RunActionAsync(type, target)) return "Não executou (veja o Registro)";
                    Log($"Celular -> {Actions.Describe(type, target)}");
                    return "✔ " + (target.Length > 0 ? target : Actions.Label(type));
                }
            }
            return "Botão desconhecido. Atualize a página.";
        }
        catch (Exception ex)
        {
            Log("Celular: " + ex.Message);
            return ex.Message;
        }
    }
}
