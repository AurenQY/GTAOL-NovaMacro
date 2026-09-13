using System.Collections.ObjectModel;
using System.ComponentModel;
using NovaMacro.App.Configuration;
using NovaMacro.App.Presets;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Editor;

// Editable collection state stays independent of storage and runtime services;
// the App composition layer decides when to load, save, or activate it.
internal sealed class MultiMacroEditorDraft : INotifyPropertyChanged
{
    private readonly ObservableCollection<MacroEditorDraft> _macros;
    private MacroEditorDraft? _selectedMacro;

    internal MultiMacroEditorDraft()
        : this(Array.Empty<MacroEditorDraft>())
    {
    }

    private MultiMacroEditorDraft(IEnumerable<MacroEditorDraft> macros)
    {
        _macros = new ObservableCollection<MacroEditorDraft>(macros);
        Macros = new ReadOnlyObservableCollection<MacroEditorDraft>(_macros);

        foreach (MacroEditorDraft macro in Macros)
        {
            macro.Changed += OnMacroChanged;
        }

        _selectedMacro = Macros.Count == 0 ? null : Macros[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? Changed;

    public ReadOnlyObservableCollection<MacroEditorDraft> Macros { get; }

    public MacroEditorDraft? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (value is not null && !_macros.Contains(value))
            {
                throw new ArgumentException(
                    "The selected macro must belong to this editor draft.",
                    nameof(value));
            }

            SetSelectedMacro(value);
        }
    }

    public bool HasMacros => Macros.Count != 0;

    internal static MultiMacroEditorDraft From(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new MultiMacroEditorDraft(
            settings.Macros.Select(MacroEditorDraft.From));
    }

    internal void Select(MacroEditorDraft macro)
    {
        ArgumentNullException.ThrowIfNull(macro);

        if (!_macros.Contains(macro))
        {
            throw new ArgumentException(
                "The selected macro must belong to this editor draft.",
                nameof(macro));
        }

        SetSelectedMacro(macro);
    }

    internal MacroEditorDraft AddMacro()
    {
        return Add(
            new MacroEditorDraft(
                CreateOrdinaryMacroId(),
                enabled: false,
                name: "New Macro",
                hotkeyKey: HotkeyKey.F8,
                control: false,
                alt: false,
                shift: false,
                steps: new[] { MacroStepEditorDraft.KeyTap(KeyboardKey.A) }));
    }

    internal MacroEditorDraft AddFromPreset(BuiltInPresetDefinition preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var copy = new MacroDefinition(
            CreateOrdinaryMacroId(),
            preset.Name,
            preset.Enabled,
            preset.Trigger,
            preset.ExecutionMode,
            preset.Sequence);

        return Add(MacroEditorDraft.From(copy));
    }

    private MacroEditorDraft Add(MacroEditorDraft macro)
    {
        macro.Changed += OnMacroChanged;
        _macros.Add(macro);
        OnPropertyChanged(nameof(HasMacros));
        SetSelectedMacro(macro);
        OnChanged();
        return macro;
    }

    private MacroId CreateOrdinaryMacroId()
    {
        MacroId id;

        do
        {
            id = new MacroId(Guid.NewGuid());
        }
        while (id == SingleMacroCompatibility.LegacyMacroId ||
            _macros.Any(macro => macro.Id == id));

        return id;
    }

    internal bool DeleteSelectedMacro()
    {
        if (_selectedMacro is null)
        {
            return false;
        }

        int selectedIndex = _macros.IndexOf(_selectedMacro);
        MacroEditorDraft removed = _selectedMacro;
        _macros.RemoveAt(selectedIndex);
        removed.Changed -= OnMacroChanged;

        MacroEditorDraft? nextSelection = selectedIndex < _macros.Count
            ? _macros[selectedIndex]
            : selectedIndex > 0
                ? _macros[selectedIndex - 1]
                : null;

        OnPropertyChanged(nameof(HasMacros));
        SetSelectedMacro(nextSelection);
        OnChanged();
        return true;
    }

    internal MacroEditorValidationResult Validate()
    {
        var candidates = new List<MacroDefinitionCandidate?>(Macros.Count);
        var parseErrors = new List<EditorParseError>();

        for (int index = 0; index < Macros.Count; index++)
        {
            if (!Macros[index].TryCreateMacroCandidate(
                    $"macros[{index}]",
                    out MacroDefinitionCandidate? macro,
                    out IReadOnlyList<EditorParseError> macroParseErrors))
            {
                parseErrors.AddRange(macroParseErrors);
                continue;
            }

            candidates.Add(macro);
        }

        if (parseErrors.Count != 0)
        {
            return MacroEditorValidationResult.InvalidParse(parseErrors);
        }

        ConfigurationValidationResult validation = ConfigurationValidator.Validate(
            new ApplicationSettingsCandidate(candidates));

        return validation.IsValid
            ? MacroEditorValidationResult.Valid(validation.Settings!)
            : MacroEditorValidationResult.InvalidConfiguration(validation.Errors);
    }

    internal bool TryBuildSettings(
        out ApplicationSettings? settings,
        out MacroEditorValidationResult validation)
    {
        validation = Validate();
        settings = validation.Settings;
        return validation.IsValid;
    }

    private void SetSelectedMacro(MacroEditorDraft? macro)
    {
        if (ReferenceEquals(_selectedMacro, macro))
        {
            return;
        }

        _selectedMacro = macro;
        OnPropertyChanged(nameof(SelectedMacro));
        OnChanged();
    }

    private void OnMacroChanged(object? sender, EventArgs e)
    {
        OnChanged();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
