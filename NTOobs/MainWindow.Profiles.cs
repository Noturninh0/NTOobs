using System.Windows;
using System.Windows.Controls;

namespace NTOobs;

// Perfis: um conjunto de teclas por jogo, com troca automática.
public partial class MainWindow
{
    bool _suppressProfileSel;

    Profile ActiveProfileObj =>
        _settings.Profiles.FirstOrDefault(p => p.Name == _settings.ActiveProfile) ?? _settings.Profiles[0];

    void MigrateProfiles()
    {
        if (_settings.Profiles.Count == 0)
        {
            _settings.Profiles.Add(new Profile { Name = "Padrão", Bindings = _settings.Bindings.ToList() });
            _settings.Bindings = new();
        }
        if (!_settings.Profiles.Any(p => p.Name == _settings.ActiveProfile))
            _settings.ActiveProfile = _settings.Profiles[0].Name;
    }

    void InitProfilesUi()
    {
        ChkAutoProfile.IsChecked = _settings.AutoProfile;
        RefreshProfiles();
    }

    void RefreshProfiles()
    {
        _suppressProfileSel = true;
        try
        {
            ListProfiles.ItemsSource = _settings.Profiles.ToList();
            ListProfiles.SelectedItem = ActiveProfileObj;
        }
        finally
        {
            _suppressProfileSel = false;
        }
        TxtProfileExe.Text = ActiveProfileObj.Exe;
        RefreshLists();
    }

    void LoadActiveBindings()
    {
        _bindings.Clear();
        foreach (var b in ActiveProfileObj.Bindings) _bindings.Add(b);
    }

    void SwitchProfile(string name, string why)
    {
        if (name == _settings.ActiveProfile) return;
        if (!_settings.Profiles.Any(p => p.Name == name)) return;

        ActiveProfileObj.Bindings = _bindings.ToList();
        _settings.ActiveProfile = name;
        LoadActiveBindings();
        SaveSettings();
        RefreshProfiles();
        RefreshMacroKey();
        RefreshSounds();
        Log($"Perfil ativo: {name}{why}");
    }

    void ListProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileSel) return;
        if (ListProfiles.SelectedItem is Profile p) SwitchProfile(p.Name, "");
    }

    static DeckBinding Clone(DeckBinding b) =>
        new() { VKey = b.VKey, DeviceId = b.DeviceId, Action = b.Action, Target = b.Target };

    void BtnProfileNew_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtProfileName.Text.Trim();
        if (name.Length == 0) name = UniqueName("Perfil", _settings.Profiles.Select(p => p.Name));
        if (_settings.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Já existe um perfil com esse nome.", "NTOobs");
            return;
        }

        // a tecla da música vale em todos os perfis: copia para o novo
        var profile = new Profile
        {
            Name = name,
            Bindings = _bindings.Where(b => b.Action == ActionType.MusicToggle).Select(Clone).ToList()
        };
        _settings.Profiles.Add(profile);
        TxtProfileName.Text = "";
        SwitchProfile(name, " (novo)");
    }

    void BtnProfileRename_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtProfileName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Digite o novo nome no campo ao lado.", "NTOobs");
            return;
        }
        if (_settings.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Já existe um perfil com esse nome.", "NTOobs");
            return;
        }
        var old = ActiveProfileObj.Name;
        ActiveProfileObj.Name = name;
        _settings.ActiveProfile = name;
        TxtProfileName.Text = "";
        SaveSettings();
        RefreshProfiles();
        Log($"Perfil \"{old}\" agora se chama \"{name}\".");
    }

    void BtnProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "É preciso ter pelo menos um perfil.", "NTOobs");
            return;
        }
        var p = ActiveProfileObj;
        if (MessageBox.Show(this, $"Excluir o perfil \"{p.Name}\" e todas as teclas dele?", "NTOobs",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _settings.Profiles.Remove(p);
        _settings.ActiveProfile = _settings.Profiles[0].Name;
        LoadActiveBindings();
        SaveSettings();
        RefreshProfiles();
        RefreshMacroKey();
        RefreshSounds();
        Log($"Perfil \"{p.Name}\" excluído. Ativo: {_settings.ActiveProfile}.");
    }

    void TxtProfileExe_LostFocus(object sender, RoutedEventArgs e)
    {
        var exe = GameWatcher.Normalize(TxtProfileExe.Text);
        if (exe == ActiveProfileObj.Exe) return;
        ActiveProfileObj.Exe = exe;
        SaveSettings();
        RefreshProfiles();
    }

    void BtnProfilePickGame_Click(object sender, RoutedEventArgs e)
    {
        ListProcesses.ItemsSource = GameWatcher.WindowedProcesses()
            .Select(x => $"{x.Process}   —   {x.Title}")
            .ToList();
        ListProcesses.Visibility = Visibility.Visible;
    }

    void ListProcesses_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListProcesses.SelectedItem is not string item) return;
        var exe = item.Split("   —   ")[0].Trim();
        ActiveProfileObj.Exe = exe;
        TxtProfileExe.Text = exe;
        ListProcesses.Visibility = Visibility.Collapsed;
        SaveSettings();
        RefreshProfiles();
        Log($"Perfil {ActiveProfileObj.Name} ligado ao jogo {exe}.");
    }

    void ChkAutoProfile_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoProfile = ChkAutoProfile.IsChecked == true;
        SaveSettings();
    }

    /// <summary>Se o jogo de algum perfil estiver na frente, ativa esse perfil.</summary>
    void CheckGameProfile()
    {
        if (!_settings.AutoProfile || _captureCallback != null || _capturing) return;
        var fg = GameWatcher.ForegroundProcess();
        if (fg.Length == 0) return;
        var profile = _settings.Profiles.FirstOrDefault(p =>
            p.Exe.Length > 0 && p.Exe.Equals(fg, StringComparison.OrdinalIgnoreCase));
        if (profile != null && profile.Name != _settings.ActiveProfile)
            SwitchProfile(profile.Name, $" (o jogo {fg} está na tela)");
    }
}
