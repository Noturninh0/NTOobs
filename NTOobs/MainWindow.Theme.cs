using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NTOobs;

// Aparência: tema escuro/claro (padrão) e temas de fundo importados pelo usuário.
public partial class MainWindow
{
    bool _themeLoaded;
    (int W, int H)? _themePixels;

    /// <summary>Tamanho ideal da imagem: Full HD de largura, na proporção atual da janela.</summary>
    (int W, int H) RecommendedThemeSize()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w <= 0 || h <= 0) { w = 1040; h = 700; }
        return (1920, (int)Math.Round(1920 * h / w));
    }

    void UpdateThemeSizeHint()
    {
        var (rw, rh) = RecommendedThemeSize();
        TxtThemeSize.Text = $"Tamanho recomendado: {rw} × {rh} px";

        if (_themePixels is not (int iw, int ih))
        {
            TxtThemeSizeHint.Text = "Imagem deitada, na mesma proporção da janela, cobre o app inteiro sem cortar nada. " +
                                    "Se você mudar o tamanho da janela, este número acompanha.";
            return;
        }

        double target = (double)rw / rh, actual = (double)iw / ih;
        string fit = Math.Abs(actual - target) / target < 0.12
            ? "a proporção está boa, ela cobre a janela quase sem cortes"
            : actual < target
                ? (_settings.ThemeFill ? "ela é mais alta que a janela, então o fim de baixo fica cortado"
                                       : "ela é mais alta que a janela, então sobram faixas nas laterais")
                : (_settings.ThemeFill ? "ela é mais larga que a janela, então as laterais ficam cortadas"
                                       : "ela é mais larga que a janela, então sobram faixas em cima e embaixo");
        string sharp = iw < rw * 0.6 ? " Ela é pequena para a janela e pode ficar borrada." : "";
        TxtThemeSizeHint.Text = $"A imagem atual tem {iw} × {ih} px: {fit}.{sharp}";
    }

    void ThemeFit_Checked(object sender, RoutedEventArgs e)
    {
        if (!_themeLoaded) return;
        _settings.ThemeFill = RbThemeFill.IsChecked == true;
        SaveSettings();
        ApplyThemeImage();
    }
    bool _syncingTheme;

    static string BackgroundsFolder => Path.Combine(ConfigStore.Dir, "Temas");

    void InitThemeUi()
    {
        _syncingTheme = true;
        RbThemeDark.IsChecked = _settings.DarkTheme;
        RbThemeLight.IsChecked = !_settings.DarkTheme;
        _syncingTheme = false;

        // temas cujo arquivo sumiu saem da lista
        _settings.UserThemes.RemoveAll(t => !File.Exists(t.File));
        RbThemeFill.IsChecked = _settings.ThemeFill;
        RbThemeWhole.IsChecked = !_settings.ThemeFill;
        SizeChanged += (_, _) => UpdateThemeSizeHint();

        _settings.ThemeImageOpacity = Math.Clamp(_settings.ThemeImageOpacity, 5, 100);
        SldThemeOpacity.Value = _settings.ThemeImageOpacity;
        TxtThemeOpacity.Text = $"{_settings.ThemeImageOpacity}%";

        _themeLoaded = true;
        ApplyThemeImage();
    }

    void SetBaseTheme(bool dark)
    {
        _settings.DarkTheme = dark;
        ApplyThemeColors(dark, ThemeBg.Visibility == Visibility.Visible);
        ApplyTitleBar(dark);
        BtnTheme.Content = dark ? "" : "";

        _syncingTheme = true;
        RbThemeDark.IsChecked = dark;
        RbThemeLight.IsChecked = !dark;
        _syncingTheme = false;
        SaveSettings();
    }

    void BaseTheme_Checked(object sender, RoutedEventArgs e)
    {
        if (!_themeLoaded || _syncingTheme) return;
        SetBaseTheme(RbThemeDark.IsChecked == true);
    }

    static BitmapImage LoadBitmap(string file, int decodeHeight)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(file, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad; // não trava o arquivo
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.DecodePixelHeight = decodeHeight;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    void ApplyThemeImage()
    {
        var theme = _settings.UserThemes.FirstOrDefault(t => t.File == _settings.ThemeImage);
        BitmapImage? bmp = null;
        var label = "Sem imagem de fundo: tema " + (_settings.DarkTheme ? "escuro." : "claro.");
        if (theme != null)
        {
            try
            {
                bmp = LoadBitmap(theme.File, 1400);
                label = "Tema em uso: " + theme.Name;
            }
            catch (Exception ex)
            {
                Log("Não consegui abrir a imagem do tema: " + ex.Message);
                label = "Não consegui abrir a imagem desse tema.";
            }
        }

        ThemeBg.Source = bmp;
        ThemeBg.Visibility = bmp == null ? Visibility.Collapsed : Visibility.Visible;
        if (bmp != null)
        {
            bool wide = bmp.PixelWidth > bmp.PixelHeight;
            if (_settings.ThemeFill)
            {
                // cobre a janela inteira; em imagem em pé, guarda o topo (onde costuma estar o rosto)
                ThemeBg.Stretch = Stretch.UniformToFill;
                ThemeBg.HorizontalAlignment = HorizontalAlignment.Center;
                ThemeBg.VerticalAlignment = wide ? VerticalAlignment.Center : VerticalAlignment.Top;
            }
            else
            {
                // mostra a imagem inteira: deitada no centro, em pé no canto direito
                ThemeBg.Stretch = Stretch.Uniform;
                ThemeBg.HorizontalAlignment = wide ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                ThemeBg.VerticalAlignment = wide ? VerticalAlignment.Center : VerticalAlignment.Bottom;
            }
        }
        _themePixels = bmp == null ? null : (bmp.PixelWidth, bmp.PixelHeight);
        UpdateThemeSizeHint();
        ThemeBg.Opacity = _settings.ThemeImageOpacity / 100.0;
        ApplyThemeColors(_settings.DarkTheme, bmp != null);
        TxtThemeInfo.Text = label;
        BuildThemeGallery();
    }

    void BuildThemeGallery()
    {
        PanelThemes.Children.Clear();
        TxtNoThemes.Visibility = _settings.UserThemes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var theme in _settings.UserThemes)
        {
            bool selected = theme.File == _settings.ThemeImage;
            var image = new Image { Stretch = Stretch.UniformToFill };
            try { image.Source = LoadBitmap(theme.File, 260); } catch { }

            var frame = new Border
            {
                Width = 96,
                Height = 128,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(selected ? 3 : 1),
                Child = image
            };
            frame.SetResourceReference(Border.BorderBrushProperty, selected ? "Accent" : "Line");

            var pick = new Button
            {
                Content = frame,
                Tag = theme.File,
                Padding = new Thickness(4),
                ToolTip = selected ? theme.Name + " (em uso)" : "Usar " + theme.Name
            };
            pick.Click += ThemePick_Click;

            var remove = new Button
            {
                Content = "Remover",
                Tag = theme.File,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Remover este tema"
            };
            remove.Click += ThemeRemove_Click;

            var name = new TextBlock
            {
                Text = theme.Name.Length > 14 ? theme.Name[..13] + "…" : theme.Name,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var tile = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            tile.Children.Add(pick);
            tile.Children.Add(name);
            tile.Children.Add(remove);
            PanelThemes.Children.Add(tile);
        }
    }

    void BtnThemeImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar tema (imagens de fundo)",
            Filter = "Imagens (png, jpg, webp, bmp, gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Todos os arquivos|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        UserTheme? last = null;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                LoadBitmap(file, 64); // confere se é uma imagem válida
                var dest = Soundboard.Import(file, BackgroundsFolder);
                if (_settings.UserThemes.Any(t => t.File == dest)) continue;
                last = new UserTheme
                {
                    Name = UniqueName(Path.GetFileNameWithoutExtension(file), _settings.UserThemes.Select(t => t.Name)),
                    File = dest
                };
                _settings.UserThemes.Add(last);
            }
            catch (Exception ex)
            {
                Log($"Não consegui importar {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (last != null)
        {
            _settings.ThemeImage = last.File; // já usa o último importado
            Log($"Tema importado: {last.Name}.");
        }
        SaveSettings();
        ApplyThemeImage();
    }

    void ThemePick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string file }) return;
        _settings.ThemeImage = file;
        SaveSettings();
        ApplyThemeImage();
    }

    void ThemeRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string file }) return;
        var theme = _settings.UserThemes.FirstOrDefault(t => t.File == file);
        if (theme == null) return;
        _settings.UserThemes.Remove(theme);
        if (_settings.ThemeImage == file) _settings.ThemeImage = "";
        try { File.Delete(file); } catch { } // a cópia na pasta do NTOobs; o original não é tocado
        SaveSettings();
        ApplyThemeImage();
    }

    void BtnThemeNone_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemeImage = "";
        SaveSettings();
        ApplyThemeImage();
    }

    void SldThemeOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_themeLoaded) return;
        _settings.ThemeImageOpacity = (int)Math.Round(e.NewValue);
        ThemeBg.Opacity = _settings.ThemeImageOpacity / 100.0;
        TxtThemeOpacity.Text = $"{_settings.ThemeImageOpacity}%";
        SaveSettings();
    }
}
