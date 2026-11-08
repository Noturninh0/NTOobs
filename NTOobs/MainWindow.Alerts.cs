using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NTOobs;

/// <summary>Linha de um tipo de alerta (follow, sub, raid, bits).</summary>
public sealed class AlertKindRow
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string MediaText { get; init; } = "";
    public string SoundText { get; init; } = "";
}

// Alertas: modelos prontos, posição, tempo e imagem/GIF/vídeo e som de cada tipo.
public partial class MainWindow
{
    static readonly (string Id, string Name)[] AlertPositions =
    {
        ("top", "Topo"),
        ("center", "Centro"),
        ("bottom", "Embaixo")
    };

    static string AlertsFolder => Path.Combine(ConfigStore.Dir, "Alertas");

    void InitAlertsUi()
    {
        if (!AlertsHub.Templates.Any(t => t.Id == _settings.AlertTemplate)) _settings.AlertTemplate = "classico";
        if (!AlertPositions.Any(p => p.Id == _settings.AlertPosition)) _settings.AlertPosition = "top";

        PanelAlertTemplates.Children.Clear();
        foreach (var (id, name) in AlertsHub.Templates)
        {
            var rb = new RadioButton
            {
                Content = name,
                Tag = id,
                GroupName = "AlertTemplate",
                Style = (Style)FindResource("ChoiceRadio"),
                IsChecked = id == _settings.AlertTemplate
            };
            rb.Checked += AlertTemplate_Checked;
            PanelAlertTemplates.Children.Add(rb);
        }

        PanelAlertPositions.Children.Clear();
        foreach (var (id, name) in AlertPositions)
        {
            var rb = new RadioButton
            {
                Content = name,
                Tag = id,
                GroupName = "AlertPosition",
                Style = (Style)FindResource("ChoiceRadio"),
                Margin = new Thickness(0, 0, 20, 8),
                IsChecked = id == _settings.AlertPosition
            };
            rb.Checked += AlertPosition_Checked;
            PanelAlertPositions.Children.Add(rb);
        }

        TxtAlertDuration.Text = _settings.AlertDuration.ToString();
        RefreshAlertKinds();
        ApplyAlertConfig();
    }

    /// <summary>Passa as configurações para a página de alertas do OBS (ela atualiza sozinha).</summary>
    void ApplyAlertConfig()
    {
        _alerts.Template = _settings.AlertTemplate;
        _alerts.Position = _settings.AlertPosition;
        _alerts.Duration = _settings.AlertDuration;
        _alerts.Media = new Dictionary<string, string>(_settings.AlertMedia);
        _alerts.Sounds = new Dictionary<string, string>(_settings.AlertSounds);
        _alerts.SoundFile = _settings.AlertSound;
        _alerts.Font = _settings.TextStyle.Font;
    }

    void RefreshAlertKinds()
    {
        var defaultSound = File.Exists(_settings.AlertSound) ? Path.GetFileName(_settings.AlertSound) : "";
        ListAlertKinds.ItemsSource = AlertsHub.Kinds.Select(k =>
        {
            _settings.AlertMedia.TryGetValue(k.Kind, out var media);
            _settings.AlertSounds.TryGetValue(k.Kind, out var sound);
            bool hasMedia = media != null && File.Exists(media);
            bool hasSound = sound != null && File.Exists(sound);
            return new AlertKindRow
            {
                Kind = k.Kind,
                Name = k.Name,
                MediaText = hasMedia
                    ? (AlertsHub.IsVideo(media!) ? "Vídeo: " : "Imagem: ") + Path.GetFileName(media)
                    : "Imagem: a do modelo",
                SoundText = hasSound ? "Som: " + Path.GetFileName(sound)
                    : defaultSound.Length > 0 ? $"Som: padrão ({defaultSound})"
                    : "Sem som"
            };
        }).ToList();
    }

    void AlertTemplate_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string id }) return;
        _settings.AlertTemplate = id;
        SaveSettings();
        ApplyAlertConfig();
        _alerts.Push("test", "Modelo " + AlertsHub.Templates.First(t => t.Id == id).Name, "assim vão aparecer os seus alertas");
    }

    void AlertPosition_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string id }) return;
        _settings.AlertPosition = id;
        SaveSettings();
        ApplyAlertConfig();
    }

    void AlertDuration_LostFocus(object sender, RoutedEventArgs e)
    {
        _settings.AlertDuration = int.TryParse(TxtAlertDuration.Text.Trim(), out var d) ? Math.Clamp(d, 2, 30) : 6;
        TxtAlertDuration.Text = _settings.AlertDuration.ToString();
        SaveSettings();
        ApplyAlertConfig();
    }

    void AlertKindMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AlertKindRow row }) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Imagem, GIF ou vídeo do alerta de {row.Name}",
            Filter = "Imagem, GIF ou vídeo|*.gif;*.png;*.jpg;*.jpeg;*.webp;*.mp4;*.webm|Todos os arquivos|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var size = new FileInfo(dialog.FileName).Length;
            if (size > 50L * 1024 * 1024)
            {
                MessageBox.Show(this, "Esse arquivo tem mais de 50 MB. Use um GIF ou vídeo menor (alguns segundos bastam).", "NTOobs");
                return;
            }
            _settings.AlertMedia[row.Kind] = Soundboard.Import(dialog.FileName, AlertsFolder);
            SaveSettings();
            ApplyAlertConfig();
            RefreshAlertKinds();
            PushSampleAlert(row.Kind);
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    void AlertKindSound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AlertKindRow row }) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Som do alerta de {row.Name}",
            Filter = "Áudio (mp3, wav, ogg)|*.mp3;*.wav;*.ogg"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _settings.AlertSounds[row.Kind] = Soundboard.Import(dialog.FileName, AlertsFolder);
            SaveSettings();
            ApplyAlertConfig();
            RefreshAlertKinds();
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    void AlertKindClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AlertKindRow row }) return;
        _settings.AlertMedia.Remove(row.Kind);
        _settings.AlertSounds.Remove(row.Kind);
        SaveSettings();
        ApplyAlertConfig();
        RefreshAlertKinds();
    }

    void AlertKindTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AlertKindRow row }) PushSampleAlert(row.Kind);
    }

    void PushSampleAlert(string kind)
    {
        var sample = AlertsHub.Kinds.FirstOrDefault(k => k.Kind == kind);
        if (sample.Kind == null) return;
        _alerts.Push(sample.Kind, sample.Title, sample.Detail);
        Log($"Alerta de teste ({sample.Name}) enviado.");
    }

    void BtnAlertPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!_musicReady)
        {
            MessageBox.Show(this, "O servidor local do NTOobs não iniciou. Feche outros programas e abra o NTOobs de novo.", "NTOobs");
            return;
        }
        OpenUrl($"http://127.0.0.1:{_music.Port}/alerts?preview=1");
    }
}
