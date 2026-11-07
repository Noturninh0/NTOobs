using System.IO;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NTOobs;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static readonly Dictionary<string, string> DarkColors = new()
    {
        ["Bg"] = "#0A0A0B", ["Card"] = "#0F0F11", ["Line"] = "#222226", ["Text"] = "#F2F2F3",
        ["Muted"] = "#8C8C96", ["Accent"] = "#F5A524", ["Danger"] = "#E8735C", ["Ok"] = "#34D399",
        ["Hover"] = "#1B1B1F", ["Input"] = "#131316"
    };

    static readonly Dictionary<string, string> LightColors = new()
    {
        ["Bg"] = "#F5F5F7", ["Card"] = "#FFFFFF", ["Line"] = "#E2E2E6", ["Text"] = "#111113",
        ["Muted"] = "#6B6B75", ["Accent"] = "#D97706", ["Danger"] = "#D9482B", ["Ok"] = "#059669",
        ["Hover"] = "#EDEDF0", ["Input"] = "#FFFFFF"
    };

    readonly ObsClient _obs = new();
    readonly RawInputListener _raw = new();
    readonly Settings _settings = ConfigStore.Load();
    readonly ObservableCollection<DeckBinding> _bindings = new();
    readonly ObservableCollection<string> _scenes = new();
    readonly ObservableCollection<string> _hotkeyView = new();
    readonly List<string> _allHotkeys = new();

    ActionType _action = ActionType.Scene;
    bool _capturing;
    bool _capturingCombo;
    bool _capturingMusic;
    readonly HashSet<ushort> _heldMods = new();
    ushort _capVKey;
    string _capDevice = "";

    bool _wantConnected;
    bool _reconnecting;
    string? _lastConnectError;

    readonly MusicServer _music = new();
    bool _musicReady;
    bool _musicLoaded;
    DispatcherTimer? _musicTimer;

    public MainWindow()
    {
        ApplyThemeColors(_settings.DarkTheme);
        InitializeComponent();

        TxtHost.Text = _settings.Host;
        TxtPort.Text = _settings.Port.ToString();
        PwdBox.Password = _settings.Password;
        ChkAuto.IsChecked = _settings.AutoConnect;
        BtnTheme.Content = _settings.DarkTheme ? "" : "";

        foreach (var b in _settings.Bindings) _bindings.Add(b);
        _bindings.CollectionChanged += (_, _) => RefreshLists();
        PanelList.ItemsSource = _bindings;
        ListBindings.ItemsSource = _bindings;
        ListScenes.ItemsSource = _scenes;
        RefreshLists();

        // música da live: o player roda dentro do OBS; aqui só o servidor local e os controles
        TxtMusicUrl.Text = _settings.MusicUrl;
        ChkMusicLoop.IsChecked = _settings.MusicLoop;
        ChkMusicShuffle.IsChecked = _settings.MusicShuffle;
        SldVolume.Value = _settings.MusicVolume;
        TxtVolValue.Text = $"{_settings.MusicVolume}%";
        _music.Volume = _settings.MusicVolume;
        _music.Changed += () => Dispatcher.InvokeAsync(UpdateMusicStatus);
        try { _musicReady = _music.Start(47633); }
        catch (Exception ex) { Log("Erro ao iniciar o servidor de música: " + ex.Message); }
        _musicTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _musicTimer.Tick += (_, _) => UpdateMusicStatus();
        _musicTimer.Start();
        _musicLoaded = true;
        UpdateMusicStatus();

        RbScene.IsChecked = true;
        NavPanel.IsChecked = true;

        _obs.Disconnected += () => Dispatcher.Invoke(() =>
        {
            SetStatus("Desconectado", false);
            Log("Conexão com o OBS caiu. Tentando reconectar...");
            if (_wantConnected) _ = ReconnectLoopAsync();
        });

        SourceInitialized += (_, _) =>
        {
            ApplyTitleBar(_settings.DarkTheme);
            try
            {
                _raw.Attach(this);
                _raw.KeyPressed += OnKeyPressed;
                _raw.KeyReleased += (vk, _) => _heldMods.Remove(vk);
            }
            catch (Exception ex)
            {
                Log("Erro: " + ex.Message);
            }
        };

        Loaded += (_, _) =>
        {
            Log("Pronto. Configure a conexão e adicione seus atalhos.");
            if (_settings.AutoConnect)
            {
                _wantConnected = true;
                _ = ReconnectLoopAsync();
            }
        };

        PreviewKeyDown += (_, e) => { if (_capturing || _capturingCombo || _capturingMusic) e.Handled = true; };
        Closing += (_, _) =>
        {
            _settings.MusicVolume = (int)SldVolume.Value;
            SaveSettings();
            _obs.Dispose();
            _music.Dispose(); // a música já carregada continua tocando no OBS
        };
    }

    // ---------- Tema e navegação ----------

    static void ApplyThemeColors(bool dark)
    {
        var colors = dark ? DarkColors : LightColors;
        foreach (var kv in colors)
            Application.Current.Resources[kv.Key] =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
    }

    void ApplyTitleBar(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        try { DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); } catch { }
    }

    void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        _settings.DarkTheme = !_settings.DarkTheme;
        ApplyThemeColors(_settings.DarkTheme);
        ApplyTitleBar(_settings.DarkTheme);
        BtnTheme.Content = _settings.DarkTheme ? "" : "";
        SaveSettings();
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !int.TryParse(rb.Tag?.ToString(), out var page)) return;
        PagePanel.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageShortcuts.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageConnection.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageMusic.Visibility = page == 4 ? Visibility.Visible : Visibility.Collapsed;
        PageLog.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshLists()
    {
        var empty = _bindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtEmptyPanel.Visibility = empty;
        TxtEmptyList.Visibility = empty;
        TxtFiredTotal.Text = _bindings.Sum(b => b.Fired).ToString();
        RefreshMusicKeyLabel();
    }

    // ---------- Conexão ----------

    async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_wantConnected || _obs.IsConnected)
        {
            _wantConnected = false;
            await _obs.DisconnectAsync();
            SetStatus("Desconectado", false);
            return;
        }

        try
        {
            SetStatus("Conectando...", false);
            await ConnectCoreAsync();
            _wantConnected = true;
            SetStatus("Conectado", true);
        }
        catch (Exception ex)
        {
            SetStatus("Falha ao conectar", false);
            Log("Erro: " + ex.Message);
        }
    }

    async Task ConnectCoreAsync()
    {
        _ = int.TryParse(TxtPort.Text, out var port);
        if (port <= 0) port = 4455;

        await _obs.ConnectAsync(TxtHost.Text.Trim(), port, PwdBox.Password);
        SetStatus("Conectado", true);
        SaveSettings();
        await RefreshScenesAsync();
        await RefreshHotkeysAsync();
        _lastConnectError = null;
        await EnsureMusicAsync();
    }

    async Task ReconnectLoopAsync()
    {
        if (_reconnecting) return;
        _reconnecting = true;
        try
        {
            while (_wantConnected && !_obs.IsConnected)
            {
                try
                {
                    SetStatus("Conectando...", false);
                    await ConnectCoreAsync();
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus("Aguardando o OBS...", false);
                    if (ex.Message != _lastConnectError)
                    {
                        _lastConnectError = ex.Message;
                        Log("Não conectou ao OBS: " + ex.Message);
                    }
                }
                await Task.Delay(3000);
            }
        }
        finally
        {
            _reconnecting = false;
        }
    }

    void SetStatus(string text, bool ok)
    {
        var key = ok ? "Ok" : "Danger";
        TxtStatusPill.Text = text;
        TxtStatusBig.Text = text;
        PillDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, key);
        BtnConnect.Content = (ok || _wantConnected) ? "Desconectar" : "Conectar";
    }

    async Task RefreshScenesAsync()
    {
        try
        {
            var data = await _obs.RequestAsync("GetSceneList");
            _scenes.Clear();
            foreach (var s in data.GetProperty("scenes").EnumerateArray()
                         .OrderByDescending(s => s.GetProperty("sceneIndex").GetInt32()))
                _scenes.Add(s.GetProperty("sceneName").GetString() ?? "");
            Log($"{_scenes.Count} cena(s) carregada(s) do OBS.");
        }
        catch (Exception ex)
        {
            Log("Erro ao listar cenas: " + ex.Message);
        }
    }

    async Task RefreshHotkeysAsync()
    {
        try
        {
            var data = await _obs.RequestAsync("GetHotkeyList");
            _allHotkeys.Clear();
            foreach (var h in data.GetProperty("hotkeys").EnumerateArray())
            {
                var name = h.GetString();
                if (!string.IsNullOrEmpty(name)) _allHotkeys.Add(name);
            }
            _allHotkeys.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyHotkeyFilter();
            Log($"{_allHotkeys.Count} atalho(s) do OBS carregado(s) (inclui os de plugins).");
        }
        catch (Exception ex)
        {
            Log("Erro ao listar atalhos do OBS: " + ex.Message);
        }
    }

    void ApplyHotkeyFilter()
    {
        var filter = TxtFilter.Text.Trim();
        _hotkeyView.Clear();
        foreach (var h in _allHotkeys)
            if (filter.Length == 0 || h.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _hotkeyView.Add(h);
    }

    void TxtFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyHotkeyFilter();

    async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshScenesAsync();
        await RefreshHotkeysAsync();
    }

    void SaveSettings()
    {
        _settings.Host = TxtHost.Text.Trim();
        _settings.Port = int.TryParse(TxtPort.Text, out var p) && p > 0 ? p : 4455;
        _settings.Password = PwdBox.Password;
        _settings.AutoConnect = ChkAuto.IsChecked == true;
        _settings.Bindings = _bindings.ToList();
        ConfigStore.Save(_settings);
    }

    // ---------- Teclas ----------

    static bool IsModifier(ushort vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    static string? ComboKeyName(ushort vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);
        return vk switch
        {
            0x20 => "Space", 0x0D => "Enter", 0x1B => "Esc", 0x09 => "Tab", 0x08 => "Backspace",
            0x2D => "Insert", 0x2E => "Delete", 0x24 => "Home", 0x23 => "End",
            0x21 => "PageUp", 0x22 => "PageDown", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x13 => "Pause", 0x2C => "PrintScreen",
            _ => null
        };
    }

    void BtnCaptureCombo_Click(object sender, RoutedEventArgs e)
    {
        _capturingCombo = true;
        BtnCaptureCombo.Content = "Aperte a combinação agora...";
    }

    void HandleComboCapture(ushort vkey)
    {
        var name = ComboKeyName(vkey);
        if (name == null)
        {
            Log("Essa tecla não pode ser usada em combinações. Tente outra.");
            return;
        }

        bool Held(params ushort[] keys) => keys.Any(_heldMods.Contains);
        var parts = new List<string>();
        if (Held(0x11, 0xA2, 0xA3)) parts.Add("Ctrl");
        if (Held(0x12, 0xA4, 0xA5)) parts.Add("Alt");
        if (Held(0x10, 0xA0, 0xA1)) parts.Add("Shift");
        if (Held(0x5B, 0x5C)) parts.Add("Win");
        parts.Add(name);

        TxtTarget.Text = string.Join("+", parts);
        _capturingCombo = false;
        BtnCaptureCombo.Content = "Capturar combinação (aperte as teclas)";
    }

    void OnKeyPressed(ushort vkey, string deviceId)
    {
        if (IsModifier(vkey)) _heldMods.Add(vkey);

        if (_capturingMusic)
        {
            _capturingMusic = false;
            SetMusicKey(vkey, ChkMusicOnlyDevice.IsChecked == true ? deviceId : "");
            return;
        }

        if (_capturingCombo)
        {
            if (!IsModifier(vkey)) HandleComboCapture(vkey);
            return;
        }

        if (_capturing)
        {
            _capturing = false;
            _capVKey = vkey;
            _capDevice = deviceId;
            var keyName = KeyInterop.KeyFromVirtualKey(vkey).ToString();
            TxtCaptured.Text = $"{keyName}   [{(string.IsNullOrEmpty(deviceId) ? "teclado desconhecido" : deviceId)}]";
            TxtCaptured.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            ChkOnlyDevice.IsEnabled = !string.IsNullOrEmpty(deviceId);
            ChkOnlyDevice.IsChecked = false;
            BtnCapture.Content = "Capturar tecla";
            return;
        }

        // não dispara ações enquanto você digita nos campos do próprio app
        if (IsActive && Keyboard.FocusedElement is TextBoxBase or PasswordBox) return;

        foreach (var b in _bindings.ToList())
        {
            if (b.VKey != vkey) continue;
            if (!string.IsNullOrEmpty(b.DeviceId) && b.DeviceId != deviceId) continue;
            _ = RunAsync(b);
        }
    }

    async Task RunAsync(DeckBinding b)
    {
        try
        {
            switch (b.Action)
            {
                case ActionType.Scene:
                    await _obs.RequestAsync("SetCurrentProgramScene", new { sceneName = b.Target });
                    break;
                case ActionType.ToggleMute:
                    await _obs.RequestAsync("ToggleInputMute", new { inputName = b.Target });
                    break;
                case ActionType.ToggleRecord:
                    await _obs.RequestAsync("ToggleRecord");
                    break;
                case ActionType.ToggleStream:
                    await _obs.RequestAsync("ToggleStream");
                    break;
                case ActionType.Hotkey:
                    await _obs.RequestAsync("TriggerHotkeyByName", new { hotkeyName = b.Target });
                    break;
                case ActionType.MusicToggle:
                    if (!ToggleMusic()) return;
                    break;
                case ActionType.KeyCombo:
                case ActionType.Backtrack:
                {
                    var (keyId, m) = ObsKeys.Parse(b.Target);
                    await _obs.RequestAsync("TriggerHotkeyByKeySequence", new
                    {
                        keyId,
                        keyModifiers = new { shift = m.Shift, control = m.Control, alt = m.Alt, command = m.Command }
                    });
                    break;
                }
            }
            b.Fired++;
            RefreshLists();
            Log($"{b.KeyName} -> {b.ActionLabel} {b.Target}".TrimEnd());
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    // ---------- Editor de atalhos ----------

    void BtnCapture_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        TxtCaptured.Text = "Pressione a tecla agora...";
        TxtCaptured.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        BtnCapture.Content = "Aguardando tecla...";
    }

    void Action_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !Enum.TryParse<ActionType>(rb.Tag?.ToString(), out var type)) return;
        _action = type;
        var opt = Actions.Get(type);
        TxtTargetLabel.Text = opt.TargetHint;
        TxtTarget.IsEnabled = opt.NeedsTarget;
        TxtTarget.Text = opt.DefaultTarget;

        // a lista ao lado mostra cenas ou os atalhos do OBS, conforme a ação
        bool hotkeys = type == ActionType.Hotkey;
        ListScenes.ItemsSource = hotkeys ? _hotkeyView : _scenes;
        ListScenes.IsEnabled = type is ActionType.Scene or ActionType.Hotkey;
        TxtFilter.Visibility = hotkeys ? Visibility.Visible : Visibility.Collapsed;
        _capturingCombo = false;
        BtnCaptureCombo.Content = "Capturar combinação (aperte as teclas)";
        BtnCaptureCombo.Visibility = type is ActionType.Backtrack or ActionType.KeyCombo
            ? Visibility.Visible : Visibility.Collapsed;
        TxtListLabel.Text = hotkeys
            ? "Atalhos do OBS (clique para usar)"
            : "Cenas do OBS (clique para usar)";
    }

    void ListScenes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListScenes.SelectedItem is string s) TxtTarget.Text = s;
    }

    void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_capVKey == 0)
        {
            MessageBox.Show(this, "Capture uma tecla primeiro.", "NTOobs");
            return;
        }

        var opt = Actions.Get(_action);
        var target = TxtTarget.Text.Trim();
        if (opt.NeedsTarget && target.Length == 0)
        {
            MessageBox.Show(this, "Informe o destino (nome da cena ou da fonte).", "NTOobs");
            return;
        }

        if (_action is ActionType.KeyCombo or ActionType.Backtrack)
        {
            try { ObsKeys.Parse(target); }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, "NTOobs");
                return;
            }
        }

        _bindings.Add(new DeckBinding
        {
            VKey = _capVKey,
            DeviceId = ChkOnlyDevice.IsChecked == true ? _capDevice : "",
            Action = _action,
            Target = opt.NeedsTarget ? target : ""
        });
        SaveSettings();
        Log("Atalho adicionado.");

        _capVKey = 0;
        _capDevice = "";
        TxtCaptured.Text = "Nenhuma tecla capturada";
        TxtCaptured.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        ChkOnlyDevice.IsChecked = false;
        ChkOnlyDevice.IsEnabled = false;
    }

    // ---------- Botões das linhas ----------

    void RowView_Click(object sender, RoutedEventArgs e) => NavShortcuts.IsChecked = true;

    async void RowTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeckBinding b }) await RunAsync(b);
    }

    void RowRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeckBinding b })
        {
            _bindings.Remove(b);
            SaveSettings();
        }
    }

    // ---------- Música da live ----------

    // Spotify toca pelo aplicativo do computador (músicas inteiras); YouTube pelo player no OBS.
    bool IsSpotifyMode =>
        _settings.MusicEnabled
        && MusicLinks.TryParse(_settings.MusicUrl, out var l, out _)
        && l?.Kind == "sp";

    bool ToggleMusic()
    {
        if (IsSpotifyMode)
        {
            if (!SpotifyDesktop.TogglePlayPause())
            {
                Log("O Spotify (aplicativo) não está aberto. Use Iniciar / aplicar na aba Música.");
                return false;
            }
            _ = Task.Delay(1200).ContinueWith(_ => Dispatcher.InvokeAsync(UpdateMusicStatus));
            return true;
        }

        if (!_musicReady)
        {
            Log("O servidor de música local não iniciou.");
            return false;
        }
        if (_music.SecondsSinceBeat > 12)
        {
            Log("O player de música não está respondendo no OBS. Use Iniciar / aplicar na aba Música.");
            return false;
        }
        _music.Command("toggle");
        return true;
    }

    async Task EnsureMusicAsync()
    {
        if (!_settings.MusicEnabled || !_obs.IsConnected) return;

        if (!MusicLinks.TryParse(_settings.MusicUrl, out var link, out var error) || link == null)
        {
            Log("Música: " + error);
            return;
        }

        if (link.Kind == "sp")
        {
            try
            {
                await MusicObs.EnsureSpotifyAsync(_obs, Log);
            }
            catch (Exception ex)
            {
                Log("Erro ao criar a captura do Spotify no OBS: " + ex.Message);
            }
            return;
        }

        if (!_musicReady) return;
        try
        {
            var url = _music.BuildPlayerUrl(link, _settings.MusicLoop, _settings.MusicShuffle);
            await MusicObs.EnsureAsync(_obs, url, Log);
        }
        catch (Exception ex)
        {
            Log("Erro na música: " + ex.Message);
            return;
        }

        _ = ReloadPlayerIfSilentAsync();
    }

    // Se o OBS foi aberto com o NTOobs fechado, a página do player não carregou: recarrega.
    async Task ReloadPlayerIfSilentAsync()
    {
        await Task.Delay(9000);
        if (!_settings.MusicEnabled || IsSpotifyMode || !_obs.IsConnected || _music.SecondsSinceBeat <= 12) return;
        try
        {
            await MusicObs.RefreshAsync(_obs);
            Log("O player de música não respondeu. Recarreguei a fonte no OBS.");
        }
        catch (Exception ex)
        {
            Log("Erro ao recarregar a música: " + ex.Message);
        }
    }

    // Abre a playlist no aplicativo Spotify e confere se começou a tocar.
    async Task LaunchSpotifyAsync(MusicLink link)
    {
        try
        {
            SpotifyDesktop.Open(link);
            Log("Abrindo a playlist no aplicativo Spotify...");
        }
        catch (Exception ex)
        {
            Log("Não consegui abrir o Spotify. Ele está instalado? " + ex.Message);
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(1000);
            if (SpotifyDesktop.GetState().Playing)
            {
                Log("Spotify tocando: " + SpotifyDesktop.GetState().Track);
                UpdateMusicStatus();
                return;
            }
        }

        var st = SpotifyDesktop.GetState();
        Log(st.Running
            ? "A playlist abriu no Spotify. Clique no play verde dela uma vez; depois o seu atalho pausa e retoma."
            : "O Spotify não abriu. Abra o aplicativo, entre na sua conta e clique em Iniciar / aplicar de novo.");
        UpdateMusicStatus();
    }

    void UpdateMusicStatus()
    {
        string text;
        if (!_settings.MusicEnabled)
        {
            text = "Música desligada.";
        }
        else if (IsSpotifyMode)
        {
            var st = SpotifyDesktop.GetState();
            text = !st.Running ? "Spotify (aplicativo) não está aberto."
                 : st.Playing ? "Tocando  ·  " + st.Track
                 : "Pausado (Spotify aberto)";
        }
        else if (!_musicReady)
        {
            text = "Não consegui iniciar o servidor de música local (porta ocupada?).";
        }
        else if (_music.SecondsSinceBeat > 12)
        {
            text = "Aguardando o player no OBS...";
        }
        else
        {
            var st = _music.State;
            if (st.Length > 0) st = char.ToUpperInvariant(st[0]) + st[1..];
            text = st.Length == 0 ? "Player ativo" : st;
            if (st.StartsWith("Erro"))
                text += ": o YouTube não permite tocar esse vídeo/playlist incorporado, ou o link não é aceito.";
            if (_music.Title.Length > 0) text += "  ·  " + _music.Title;
        }
        TxtMusicStatus.Text = text;
    }

    void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_musicLoaded) return;
        TxtVolValue.Text = $"{(int)e.NewValue}%";
        _music.SetVolume((int)e.NewValue);
    }

    void SldVolume_Done(object sender, MouseEventArgs e)
    {
        _settings.MusicVolume = (int)SldVolume.Value;
        SaveSettings();
    }

    async void BtnMusicStart_Click(object sender, RoutedEventArgs e)
    {
        if (!MusicLinks.TryParse(TxtMusicUrl.Text, out var link, out var error) || link == null)
        {
            MessageBox.Show(this, error, "NTOobs");
            return;
        }
        bool spotify = link.Kind == "sp";
        if (!spotify && !_musicReady)
        {
            MessageBox.Show(this, "O servidor de música local não iniciou. Feche outros programas e abra o NTOobs de novo.", "NTOobs");
            return;
        }
        if (!_obs.IsConnected)
        {
            MessageBox.Show(this, "Conecte ao OBS primeiro (aba Conexão).", "NTOobs");
            return;
        }

        _settings.MusicUrl = TxtMusicUrl.Text.Trim();
        _settings.MusicLoop = ChkMusicLoop.IsChecked == true;
        _settings.MusicShuffle = ChkMusicShuffle.IsChecked == true;
        _settings.MusicVolume = (int)SldVolume.Value;
        _settings.MusicEnabled = true;
        SaveSettings();

        // só uma fonte de música por vez no OBS
        await MusicObs.RemoveAsync(_obs, spotify ? MusicObs.InputName : MusicObs.SpotifyInputName);

        await EnsureMusicAsync();
        UpdateMusicStatus();

        if (spotify) await LaunchSpotifyAsync(link);
    }

    void BtnMusicToggle_Click(object sender, RoutedEventArgs e) => ToggleMusic();

    void BtnMusicKey_Click(object sender, RoutedEventArgs e)
    {
        _capturingMusic = true;
        BtnMusicKey.Content = "Aperte a tecla agora...";
    }

    void SetMusicKey(ushort vkey, string deviceId)
    {
        foreach (var old in _bindings.Where(b => b.Action == ActionType.MusicToggle).ToList())
            _bindings.Remove(old);

        var binding = new DeckBinding { VKey = vkey, DeviceId = deviceId, Action = ActionType.MusicToggle };
        _bindings.Add(binding);
        SaveSettings();
        BtnMusicKey.Content = "Definir tecla";
        Log($"Tecla de pausar/retomar a música: {binding.KeyName} ({binding.DeviceLabel}).");
    }

    void BtnMusicKeyClear_Click(object sender, RoutedEventArgs e)
    {
        _capturingMusic = false;
        BtnMusicKey.Content = "Definir tecla";
        foreach (var old in _bindings.Where(b => b.Action == ActionType.MusicToggle).ToList())
            _bindings.Remove(old);
        SaveSettings();
    }

    void RefreshMusicKeyLabel()
    {
        var b = _bindings.FirstOrDefault(x => x.Action == ActionType.MusicToggle);
        if (b == null)
        {
            TxtMusicKey.Text = "Nenhuma tecla definida";
            TxtMusicKey.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        }
        else
        {
            TxtMusicKey.Text = $"{b.KeyName}  ·  {b.DeviceLabel}";
            TxtMusicKey.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        }
    }

    async void BtnMusicStop_Click(object sender, RoutedEventArgs e)
    {
        bool wasSpotify = IsSpotifyMode;
        _settings.MusicEnabled = false;
        SaveSettings();

        if (wasSpotify && SpotifyDesktop.GetState().Playing)
            SpotifyDesktop.TogglePlayPause();

        if (_obs.IsConnected)
        {
            await MusicObs.RemoveAsync(_obs, MusicObs.InputName);
            await MusicObs.RemoveAsync(_obs, MusicObs.SpotifyInputName);
            Log("Música removida do OBS.");
        }
        UpdateMusicStatus();
    }

    // ---------- Registro ----------

    void Log(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => Log(text));
            return;
        }
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        TxtLog.ScrollToEnd();
    }
}
