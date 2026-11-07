using System.IO;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

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

    ActionType _action = ActionType.Scene;
    bool _capturing;
    ushort _capVKey;
    string _capDevice = "";

    bool _wantConnected;
    bool _reconnecting;

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

        PreviewKeyDown += (_, e) => { if (_capturing) e.Handled = true; };
        Closing += (_, _) => _obs.Dispose();
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
        PageLog.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshLists()
    {
        var empty = _bindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtEmptyPanel.Visibility = empty;
        TxtEmptyList.Visibility = empty;
        TxtFiredTotal.Text = _bindings.Sum(b => b.Fired).ToString();
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
                catch
                {
                    SetStatus("Aguardando o OBS...", false);
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

    async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshScenesAsync();

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

    void OnKeyPressed(ushort vkey, string deviceId)
    {
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
        ListScenes.IsEnabled = opt.NeedsTarget;
        if (!opt.NeedsTarget) TxtTarget.Text = "";
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
