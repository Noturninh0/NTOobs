using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NTOobs;

// Estilo dos textos que o NTOobs cria no OBS: tipo de letra, tamanho, cor e contorno.
public partial class MainWindow
{
    bool _styleLoaded;
    List<string> _allFonts = new();

    static readonly string[] Swatches =
    {
        "#FFFFFF", "#000000", "#F5A524", "#FACC15", "#EF4444", "#E8735C",
        "#34D399", "#38BDF8", "#3B82F6", "#9146FF", "#F472B6", "#A3A3A3"
    };

    TextStyle TextCfg => _settings.TextStyle;

    static SolidColorBrush BrushFrom(string hex)
    {
        var h = (TextStyle.NormalizeHex(hex) ?? "#FFFFFF")[1..];
        return new SolidColorBrush(Color.FromRgb(
            Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16)));
    }

    void InitTextStyleUi()
    {
        try
        {
            _allFonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            _allFonts = new List<string> { "Segoe UI", "Arial", "Impact", "Verdana", "Tahoma", "Consolas" };
        }

        PanelColors.Children.Clear();
        foreach (var hex in Swatches)
        {
            var swatch = new Button
            {
                Tag = hex,
                ToolTip = hex,
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 6, 6),
                Content = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(4),
                    Background = BrushFrom(hex),
                    BorderBrush = BrushFrom("#808080"),
                    BorderThickness = new Thickness(1)
                }
            };
            swatch.Click += Swatch_Click;
            PanelColors.Children.Add(swatch);
        }

        var st = TextCfg;
        TxtTextColor.Text = st.Color;
        ChkTextBold.IsChecked = st.Bold;
        ChkTextOutline.IsChecked = st.Outline;
        TxtOutlineColor.Text = st.OutlineColor;
        TxtOutlineSize.Text = st.OutlineSize.ToString();
        SldFontSize.Value = st.Size;
        TxtFontSizeValue.Text = st.Size.ToString();
        ApplyFontFilter();

        _styleLoaded = true;
        UpdateFontPreview();
        _alerts.Font = st.Font;
    }

    void ApplyFontFilter()
    {
        var filter = TxtFontFilter.Text.Trim();
        var list = _allFonts.Where(f => filter.Length == 0 || f.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        ListFonts.ItemsSource = list;
        var current = list.FirstOrDefault(f => f.Equals(TextCfg.Font, StringComparison.OrdinalIgnoreCase));
        if (current != null) ListFonts.SelectedItem = current;
    }

    void TxtFontFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_styleLoaded) ApplyFontFilter();
    }

    void ListFonts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_styleLoaded || ListFonts.SelectedItem is not string font || font == TextCfg.Font) return;
        TextCfg.Font = font;
        SaveTextStyle();
    }

    void SldFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_styleLoaded) return;
        TextCfg.Size = (int)Math.Round(e.NewValue);
        TxtFontSizeValue.Text = TextCfg.Size.ToString();
        SaveTextStyle();
    }

    void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex }) return;
        TxtTextColor.Text = hex;
        TextCfg.Color = hex;
        SaveTextStyle();
    }

    void TextStyle_LostFocus(object sender, RoutedEventArgs e) => ReadTextStyleFields();
    void TextStyle_Changed(object sender, RoutedEventArgs e) => ReadTextStyleFields();

    void ReadTextStyleFields()
    {
        if (!_styleLoaded) return;
        var st = TextCfg;
        st.Color = TextStyle.NormalizeHex(TxtTextColor.Text) ?? st.Color;
        st.OutlineColor = TextStyle.NormalizeHex(TxtOutlineColor.Text) ?? st.OutlineColor;
        st.OutlineSize = int.TryParse(TxtOutlineSize.Text.Trim(), out var o) ? Math.Clamp(o, 1, 20) : st.OutlineSize;
        st.Bold = ChkTextBold.IsChecked == true;
        st.Outline = ChkTextOutline.IsChecked == true;
        TxtTextColor.Text = st.Color;
        TxtOutlineColor.Text = st.OutlineColor;
        TxtOutlineSize.Text = st.OutlineSize.ToString();
        SaveTextStyle();
    }

    void SaveTextStyle()
    {
        SaveSettings();
        UpdateFontPreview();
        _alerts.Font = TextCfg.Font;
    }

    void UpdateFontPreview()
    {
        var st = TextCfg;
        try { TxtFontPreview.FontFamily = new FontFamily(st.Font); } catch { }
        TxtFontPreview.FontSize = Math.Clamp(st.Size, 12, 72);
        TxtFontPreview.FontWeight = st.Bold ? FontWeights.Bold : FontWeights.Normal;
        TxtFontPreview.Foreground = BrushFrom(st.Color);
        TxtFontPreview.Text = _settings.Counters.Count > 0 ? _settings.Counters[0].Render() : "Mortes: 7";
    }

    async void BtnApplyTextStyle_Click(object sender, RoutedEventArgs e)
    {
        ReadTextStyleFields();
        if (!_obs.IsConnected)
        {
            MessageBox.Show(this, "Conecte ao OBS primeiro (aba Conexão). O estilo já foi salvo e vale para os próximos textos.", "NTOobs");
            return;
        }

        int done = 0;
        foreach (var c in _settings.Counters.Where(c => c.Source.Length > 0).ToList())
        {
            try
            {
                await ObsTools.ApplyStyleAsync(_obs, c.Source, TextCfg);
                done++;
            }
            catch (Exception ex)
            {
                Log($"Estilo no contador {c.Name}: {ex.Message}");
            }
        }
        if (await MusicObs.InputExistsAsync(_obs, GoLiveRunner.CountdownInput))
        {
            try
            {
                await ObsTools.ApplyStyleAsync(_obs, GoLiveRunner.CountdownInput, TextCfg.Scaled(1.5));
                done++;
            }
            catch (Exception ex)
            {
                Log("Estilo na contagem: " + ex.Message);
            }
        }
        Log(done == 0
            ? "Nenhum texto do NTOobs no OBS ainda. Use Mostrar na live num contador."
            : $"Estilo aplicado em {done} texto(s) no OBS.");
    }
}
