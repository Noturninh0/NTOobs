using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NTOobs;

/// <summary>Linha da lista de contadores.</summary>
public sealed class CounterRow
{
    public CounterRow(CounterItem item, string keys)
    {
        Item = item;
        Keys = keys;
    }

    public CounterItem Item { get; }
    public string Keys { get; }
    public string Text => Item.Render();

    public string Info =>
        (Item.Source.Length > 0 ? $"Na live: fonte \"{Item.Source}\"" : "Ainda não aparece na live") +
        (Keys.Length > 0 ? "  ·  teclas " + Keys : "");
}

// Ferramentas: contadores, marcar momento, replay instantâneo e começar live.
public partial class MainWindow
{
    TextBox? _livePickTarget;

    void InitToolsUi()
    {
        TxtLiveStart.Text = _settings.LiveStartScene;
        TxtLiveMain.Text = _settings.LiveMainScene;
        TxtLiveMic.Text = _settings.LiveMic;
        TxtLiveCountdown.Text = _settings.LiveCountdown.ToString();
        ChkLiveRecord.IsChecked = _settings.LiveRecord;
        TxtReplayScene.Text = _settings.ReplayScene;
        TxtReplaySeconds.Text = _settings.ReplaySeconds.ToString();
        ChkHealthBeep.IsChecked = _settings.HealthBeep;
        RefreshCounters();
        RefreshMarkers();
    }

    void SaveTools()
    {
        _settings.LiveStartScene = TxtLiveStart.Text.Trim();
        _settings.LiveMainScene = TxtLiveMain.Text.Trim();
        _settings.LiveMic = TxtLiveMic.Text.Trim();
        _settings.LiveCountdown = int.TryParse(TxtLiveCountdown.Text.Trim(), out var c) ? Math.Clamp(c, 0, 3600) : 300;
        _settings.LiveRecord = ChkLiveRecord.IsChecked == true;
        _settings.ReplayScene = TxtReplayScene.Text.Trim();
        _settings.ReplaySeconds = int.TryParse(TxtReplaySeconds.Text.Trim(), out var r) ? Math.Clamp(r, 0, 120) : 0;
        _settings.HealthBeep = ChkHealthBeep.IsChecked == true;
        TxtLiveCountdown.Text = _settings.LiveCountdown.ToString();
        TxtReplaySeconds.Text = _settings.ReplaySeconds.ToString();
        SaveSettings();
    }

    void Tools_LostFocus(object sender, RoutedEventArgs e) => SaveTools();
    void Tools_Changed(object sender, RoutedEventArgs e) => SaveTools();

    void LivePickTarget_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        _livePickTarget = box;
        bool mic = ReferenceEquals(box, TxtLiveMic);
        ListLivePick.ItemsSource = mic ? _inputs.ToList() : _scenes.ToList();
        TxtLivePickLabel.Text = mic ? "Fontes do OBS (clique para usar):" : "Cenas do OBS (clique para usar):";
    }

    void ListLivePick_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_livePickTarget == null || ListLivePick.SelectedItem is not string s) return;
        _livePickTarget.Text = s;
        SaveTools();
        ListLivePick.SelectedItem = null;
    }

    // ---------- Contadores ----------

    void RefreshCounters()
    {
        ListCounters.ItemsSource = _settings.Counters
            .Select(c => new CounterRow(c, string.Join(", ", new[]
            {
                KeysFor(ActionType.CounterAdd, c.Name),
                KeysFor(ActionType.CounterSub, c.Name),
                KeysFor(ActionType.CounterReset, c.Name)
            }.Where(k => k.Length > 0))))
            .ToList();
    }

    CounterItem FindCounter(string name) =>
        _settings.Counters.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"O contador \"{name}\" não existe.");

    async Task ChangeCounterAsync(string name, int delta, bool reset)
    {
        var c = FindCounter(name);
        c.Value = reset ? 0 : c.Value + delta;
        SaveSettings();
        RefreshCounters();
        await PushCounterAsync(c);
    }

    async Task PushCounterAsync(CounterItem c)
    {
        if (c.Source.Length == 0 || !_obs.IsConnected) return;
        try
        {
            await ObsTools.SetTextAsync(_obs, c.Source, c.Render());
        }
        catch (Exception ex)
        {
            Log($"Contador {c.Name}: {ex.Message} (a fonte de texto foi apagada? Clique em Mostrar na live de novo.)");
        }
    }

    async Task PushAllCountersAsync()
    {
        foreach (var c in _settings.Counters.ToList()) await PushCounterAsync(c);
    }

    void BtnCounterAdd_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtCounterName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Digite o nome do contador (ex.: Mortes).", "NTOobs");
            return;
        }
        if (_settings.Counters.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Já existe um contador com esse nome.", "NTOobs");
            return;
        }
        var format = TxtCounterFormat.Text.Trim();
        if (!format.Contains("{n}")) format = format.Length == 0 ? "{nome}: {n}" : format + " {n}";
        _settings.Counters.Add(new CounterItem { Name = name, Format = format });
        TxtCounterName.Text = "";
        SaveSettings();
        RefreshCounters();
    }

    async void CounterAdd_Click(object sender, RoutedEventArgs e) => await CounterButtonAsync(sender, 1, false);
    async void CounterSub_Click(object sender, RoutedEventArgs e) => await CounterButtonAsync(sender, -1, false);
    async void CounterReset_Click(object sender, RoutedEventArgs e) => await CounterButtonAsync(sender, 0, true);

    async Task CounterButtonAsync(object sender, int delta, bool reset)
    {
        if (sender is not FrameworkElement { DataContext: CounterRow row }) return;
        try
        {
            await ChangeCounterAsync(row.Item.Name, delta, reset);
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    async void CounterShow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CounterRow row }) return;
        if (!_obs.IsConnected)
        {
            MessageBox.Show(this, "Conecte ao OBS primeiro (aba Conexão).", "NTOobs");
            return;
        }
        var c = row.Item;
        try
        {
            var scene = await ObsTools.GetCurrentSceneAsync(_obs);
            var source = c.Source.Length > 0 ? c.Source : "NTOobs " + c.Name;
            await ObsTools.EnsureTextAsync(_obs, scene, source, c.Render(), true, _settings.TextStyle);
            c.Source = source;
            SaveSettings();
            RefreshCounters();
            Log($"Contador \"{c.Name}\" na cena \"{scene}\". Arraste o texto para onde quiser no OBS.");
        }
        catch (Exception ex)
        {
            Log("Erro ao mostrar o contador: " + ex.Message);
        }
    }

    void CounterRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CounterRow row }) return;
        var c = row.Item;
        _settings.Counters.Remove(c);
        RemoveBindingsEverywhere(ActionType.CounterAdd, c.Name);
        RemoveBindingsEverywhere(ActionType.CounterSub, c.Name);
        RemoveBindingsEverywhere(ActionType.CounterReset, c.Name);
        SaveSettings();
        RefreshCounters();
    }

    // ---------- Marcar momento ----------

    async Task<string> AddMarkerAsync(string note)
    {
        var line = await Markers.AddAsync(_obs, note);
        Log("Momento marcado: " + line);
        RefreshMarkers();
        return line;
    }

    void RefreshMarkers()
    {
        var recent = Markers.Recent(8);
        TxtMarkers.Text = recent.Length == 0 ? "Nenhum momento marcado hoje." : string.Join(Environment.NewLine, recent);
    }

    async void BtnMarker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AddMarkerAsync("");
        }
        catch (Exception ex)
        {
            Log("Erro ao marcar: " + ex.Message);
        }
    }

    void BtnMarkersFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Markers.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Markers.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("Erro ao abrir a pasta: " + ex.Message);
        }
    }

    // ---------- Replay instantâneo ----------

    async Task RunReplayAsync()
    {
        try
        {
            await _replay.RunAsync(_obs, _settings.ReplayScene, _settings.ReplaySeconds, Log);
        }
        catch (Exception ex)
        {
            Log("Replay: " + ex.Message);
        }
    }

    void BtnReplayTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_obs.IsConnected)
        {
            MessageBox.Show(this, "Conecte ao OBS primeiro (aba Conexão).", "NTOobs");
            return;
        }
        SaveTools();
        _ = RunReplayAsync();
    }

    // ---------- Começar live ----------

    async Task GoLiveAsync(bool ask)
    {
        try
        {
            if (_goLive.CountingDown)
            {
                _goLive.Skip();
                return;
            }

            var problems = await GoLiveRunner.ChecklistAsync(_obs, _settings);
            if (problems.Count > 0)
            {
                var text = string.Join(Environment.NewLine, problems.Select(p => "• " + p));
                if (ask)
                {
                    if (MessageBox.Show(this, "Antes de começar:" + Environment.NewLine + Environment.NewLine + text +
                                              Environment.NewLine + Environment.NewLine + "Começar a live mesmo assim?",
                            "NTOobs", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                }
                else
                {
                    Log("Checklist da live: " + string.Join(" ", problems));
                    GameWatcher.Beep();
                }
            }

            await _goLive.StartAsync(_obs, _settings, Log);
        }
        catch (Exception ex)
        {
            Log("Começar live: " + ex.Message);
        }
    }

    async void BtnGoLive_Click(object sender, RoutedEventArgs e)
    {
        if (!_obs.IsConnected)
        {
            MessageBox.Show(this, "Conecte ao OBS primeiro (aba Conexão).", "NTOobs");
            return;
        }
        if (_goLive.CountingDown) return;
        SaveTools();
        await GoLiveAsync(true);
    }

    void BtnSkipCountdown_Click(object sender, RoutedEventArgs e) => _goLive.Skip();

    async void BtnEndLive_Click(object sender, RoutedEventArgs e)
    {
        if (!_obs.IsConnected) return;
        if (MessageBox.Show(this, "Encerrar a transmissão (e a gravação, se estiver gravando)?", "NTOobs",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _goLive.Skip();
        await GoLiveRunner.StopAsync(_obs, Log);
    }
}
