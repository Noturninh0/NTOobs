using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace NTOobs;

/// <summary>Linha da lista do soundboard.</summary>
public sealed class SoundRow : INotifyPropertyChanged
{
    public SoundRow(SoundItem item, string keys)
    {
        Item = item;
        Keys = keys;
    }

    public SoundItem Item { get; }
    public string Keys { get; }
    public string Name => Item.Name;

    public string Info =>
        Path.GetFileName(Item.File) + "  ·  " + (Keys.Length == 0 ? "sem tecla" : "tecla " + Keys) +
        (File.Exists(Item.File) ? "" : "  ·  ARQUIVO NÃO ENCONTRADO");

    public double Volume
    {
        get => Item.Volume;
        set
        {
            Item.Volume = (int)Math.Round(Math.Clamp(value, 0, 100));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VolumeText)));
        }
    }

    public string VolumeText => $"{Item.Volume}%";

    public event PropertyChangedEventHandler? PropertyChanged;
}

// Soundboard: sons que tocam na live por uma tecla.
public partial class MainWindow
{
    void RefreshSounds()
    {
        ChkSoundMonitor.IsChecked = _settings.SoundMonitor;
        ListSounds.ItemsSource = _settings.Sounds
            .Select(s => new SoundRow(s, KeysFor(ActionType.Sound, s.Name)))
            .ToList();
        TxtNoSounds.Visibility = _settings.Sounds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task PlaySoundAsync(string name)
    {
        var sound = _settings.Sounds.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"O som \"{name}\" não existe no soundboard.");
        RequireObs();
        await _soundboard.PlayAsync(_obs, sound, _settings.SoundMonitor, Log);
    }

    void BtnSoundAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha os sons",
            Filter = "Áudio (mp3, wav, ogg, m4a, flac)|*.mp3;*.wav;*.ogg;*.m4a;*.flac|Todos os arquivos|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        int added = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var dest = Soundboard.Import(file);
                var name = UniqueName(Path.GetFileNameWithoutExtension(file), _settings.Sounds.Select(s => s.Name));
                _settings.Sounds.Add(new SoundItem { Name = name, File = dest });
                added++;
            }
            catch (Exception ex)
            {
                Log($"Não consegui adicionar {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        SaveSettings();
        RefreshSounds();
        if (added > 0) Log($"{added} som(ns) adicionado(s) ao soundboard.");
    }

    async void BtnSoundStop_Click(object sender, RoutedEventArgs e)
    {
        if (_obs.IsConnected) await Soundboard.StopAsync(_obs);
    }

    void ChkSoundMonitor_Click(object sender, RoutedEventArgs e)
    {
        _settings.SoundMonitor = ChkSoundMonitor.IsChecked == true;
        SaveSettings();
    }

    async void SoundPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SoundRow row }) return;
        try
        {
            await PlaySoundAsync(row.Name);
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    void SoundKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SoundRow row }) return;
        var name = row.Name;
        TxtSoundHint.Text = $"Aperte a tecla para \"{name}\" agora (Esc cancela)...";
        StartCapture($"o som {name}", (vk, dev) =>
            AddBindingFor(ActionType.Sound, name, vk, ChkSoundOnlyDevice.IsChecked == true ? dev : ""));
    }

    void SoundRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SoundRow row }) return;
        _settings.Sounds.Remove(row.Item);
        RemoveBindingsEverywhere(ActionType.Sound, row.Name);
        SaveSettings();
        RefreshSounds();
    }

    void SoundVolume_Done(object sender, MouseEventArgs e) => SaveSettings();
}
