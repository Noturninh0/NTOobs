using System.Windows;
using System.Windows.Controls;

namespace NTOobs;

// Macros: várias ações em sequência numa tecla.
public partial class MainWindow
{
    Macro? _macroSel;

    void InitMacrosUi()
    {
        ListStepAction.ItemsSource = Actions.All.ToList();
        ListStepAction.SelectedIndex = 0;
        RefreshMacroList();
    }

    void RefreshMacroList(Macro? select = null)
    {
        var keep = select ?? _macroSel;
        ListMacros.ItemsSource = _settings.Macros.ToList();
        if (keep != null && _settings.Macros.Contains(keep))
        {
            ListMacros.SelectedItem = keep;
            _macroSel = keep;
        }
        else
        {
            _macroSel = null;
        }
        RefreshSteps();
        UpdateStepSuggestions();
    }

    void ListMacros_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListMacros.SelectedItem is Macro m)
        {
            _macroSel = m;
            RefreshSteps();
            UpdateStepSuggestions();
        }
    }

    void RefreshSteps()
    {
        var m = _macroSel;
        TxtMacroTitle.Text = m == null ? "Selecione ou crie uma macro" : $"Macro: {m.Name}";
        ListSteps.ItemsSource = m?.Steps.ToList();
        TxtNoSteps.Visibility = m == null || m.Steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtNoSteps.Text = m == null ? "Nenhuma macro selecionada." : "Nenhum passo ainda. Adicione abaixo.";
        RefreshMacroKey();
    }

    void RefreshMacroKey()
    {
        if (_macroSel == null)
        {
            TxtMacroKey.Text = "Nenhuma macro selecionada";
            return;
        }
        var keys = KeysFor(ActionType.Macro, _macroSel.Name);
        TxtMacroKey.Text = keys.Length == 0 ? "Nenhuma tecla" : "Tecla: " + keys;
    }

    void UpdateStepSuggestions()
    {
        if (ListStepAction.SelectedItem is not ActionOption opt) return;
        TxtStepTargetLabel.Text = opt.TargetHint.StartsWith("3. ") ? opt.TargetHint[3..] : opt.TargetHint;
        TxtStepTarget.IsEnabled = opt.NeedsTarget;
        TxtStepTarget.Text = opt.DefaultTarget;
        var items = SuggestionsFor(opt.List);
        if (opt.Type == ActionType.Macro && _macroSel != null) items.Remove(_macroSel.Name);
        ListStepSuggest.ItemsSource = items;
        ListStepSuggest.Visibility = opt.List == TargetList.None ? Visibility.Collapsed : Visibility.Visible;
    }

    void ListStepAction_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStepSuggestions();

    void ListStepSuggest_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListStepSuggest.SelectedItem is string s) TxtStepTarget.Text = s;
    }

    void BtnMacroNew_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtMacroName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Digite um nome para a macro.", "NTOobs");
            return;
        }
        if (_settings.Macros.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Já existe uma macro com esse nome.", "NTOobs");
            return;
        }
        var macro = new Macro { Name = name };
        _settings.Macros.Add(macro);
        TxtMacroName.Text = "";
        SaveSettings();
        RefreshMacroList(macro);
    }

    void BtnMacroExample_Click(object sender, RoutedEventArgs e)
    {
        string Find(IEnumerable<string> items, string fallback, params string[] words) =>
            items.FirstOrDefault(i => words.Any(w => i.Contains(w, StringComparison.OrdinalIgnoreCase))) ?? fallback;

        var scene = Find(_scenes, "Volto já", "volto", "pausa", "brb", "intervalo", "away");
        var mic = Find(_inputs, "Mic/Aux", "mic", "micro");

        var macro = new Macro
        {
            Name = UniqueName("Volto já", _settings.Macros.Select(m => m.Name)),
            Steps =
            {
                new MacroStep { Action = ActionType.SceneToggle, Target = scene },
                new MacroStep { Action = ActionType.ToggleMute, Target = mic },
                new MacroStep { Action = ActionType.MusicToggle }
            }
        };
        _settings.Macros.Add(macro);
        SaveSettings();
        RefreshMacroList(macro);
        Log($"Macro \"{macro.Name}\" criada: alterna a cena \"{scene}\", muta \"{mic}\" e pausa/retoma a música. Confira os nomes e defina uma tecla.");
    }

    void BtnMacroDelete_Click(object sender, RoutedEventArgs e)
    {
        var m = _macroSel;
        if (m == null) return;
        if (MessageBox.Show(this, $"Excluir a macro \"{m.Name}\"?", "NTOobs",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _settings.Macros.Remove(m);
        RemoveBindingsEverywhere(ActionType.Macro, m.Name);
        _macroSel = null;
        SaveSettings();
        RefreshMacroList();
    }

    void BtnStepAdd_Click(object sender, RoutedEventArgs e)
    {
        var m = _macroSel;
        if (m == null)
        {
            MessageBox.Show(this, "Crie ou selecione uma macro primeiro.", "NTOobs");
            return;
        }
        if (ListStepAction.SelectedItem is not ActionOption opt) return;

        var target = opt.NeedsTarget ? TxtStepTarget.Text.Trim() : "";
        if (opt.NeedsTarget && target.Length == 0)
        {
            MessageBox.Show(this, "Informe o destino do passo (use a lista para escolher).", "NTOobs");
            return;
        }
        if (opt.Type is ActionType.KeyCombo or ActionType.Backtrack)
        {
            try { ObsKeys.Parse(target); }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, "NTOobs");
                return;
            }
        }
        if (opt.Type == ActionType.Macro && target.Equals(m.Name, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Uma macro não pode chamar ela mesma.", "NTOobs");
            return;
        }
        if (!int.TryParse(TxtStepDelay.Text.Trim(), out var delay) || delay < 0) delay = 0;

        m.Steps.Add(new MacroStep { Action = opt.Type, Target = target, DelayMs = Math.Min(delay, 600000) });
        SaveSettings();
        RefreshMacroList(m);
    }

    void StepUp_Click(object sender, RoutedEventArgs e) => MoveStep(sender, -1);
    void StepDown_Click(object sender, RoutedEventArgs e) => MoveStep(sender, 1);

    void MoveStep(object sender, int dir)
    {
        var m = _macroSel;
        if (m == null || sender is not FrameworkElement { DataContext: MacroStep step }) return;
        int i = m.Steps.IndexOf(step), j = i + dir;
        if (i < 0 || j < 0 || j >= m.Steps.Count) return;
        (m.Steps[i], m.Steps[j]) = (m.Steps[j], m.Steps[i]);
        SaveSettings();
        RefreshSteps();
    }

    void StepRemove_Click(object sender, RoutedEventArgs e)
    {
        var m = _macroSel;
        if (m == null || sender is not FrameworkElement { DataContext: MacroStep step }) return;
        m.Steps.Remove(step);
        SaveSettings();
        RefreshMacroList(m);
    }

    void BtnMacroKey_Click(object sender, RoutedEventArgs e)
    {
        var m = _macroSel;
        if (m == null)
        {
            MessageBox.Show(this, "Selecione uma macro primeiro.", "NTOobs");
            return;
        }
        BtnMacroKey.Content = "Aperte a tecla agora...";
        var name = m.Name;
        StartCapture($"a macro {name}", (vk, dev) =>
            AddBindingFor(ActionType.Macro, name, vk, ChkMacroOnlyDevice.IsChecked == true ? dev : ""));
    }

    async void BtnMacroTest_Click(object sender, RoutedEventArgs e)
    {
        if (_macroSel == null) return;
        try
        {
            await RunActionAsync(ActionType.Macro, _macroSel.Name);
        }
        catch (Exception ex)
        {
            Log("Erro: " + ex.Message);
        }
    }

    async Task RunMacroAsync(string name, int depth)
    {
        if (depth > 3)
        {
            Log("Macro dentro de macro demais (máximo 3 níveis).");
            return;
        }
        var macro = _settings.Macros.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"A macro \"{name}\" não existe.");

        foreach (var step in macro.Steps.ToList())
        {
            try
            {
                await RunActionAsync(step.Action, step.Target, depth + 1);
            }
            catch (Exception ex)
            {
                Log($"Macro {macro.Name}, passo \"{step}\": {ex.Message}");
            }
            if (step.DelayMs > 0) await Task.Delay(step.DelayMs);
        }
    }
}
