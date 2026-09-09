using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Editor;

internal sealed class MacroEditorDraft : INotifyPropertyChanged
{
    private const long MaximumTimeSpanMilliseconds =
        long.MaxValue / TimeSpan.TicksPerMillisecond;

    private string _name;
    private HotkeyKey _hotkeyKey;
    private bool _control;
    private bool _alt;
    private bool _shift;
    private readonly ObservableCollection<MacroStepEditorDraft> _steps;

    private MacroEditorDraft(
        string name,
        HotkeyKey hotkeyKey,
        bool control,
        bool alt,
        bool shift,
        IEnumerable<MacroStepEditorDraft> steps)
    {
        _name = name;
        _hotkeyKey = hotkeyKey;
        _control = control;
        _alt = alt;
        _shift = shift;

        _steps = new ObservableCollection<MacroStepEditorDraft>(steps);
        Steps = new ReadOnlyObservableCollection<MacroStepEditorDraft>(_steps);

        foreach (MacroStepEditorDraft step in Steps)
        {
            step.Changed += OnStepChanged;
        }

        UpdateStepPositions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? Changed;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged(nameof(Name));
            OnChanged();
        }
    }

    public HotkeyKey HotkeyKey
    {
        get => _hotkeyKey;
        set
        {
            if (_hotkeyKey == value)
            {
                return;
            }

            _hotkeyKey = value;
            OnPropertyChanged(nameof(HotkeyKey));
            OnChanged();
        }
    }

    public bool Control
    {
        get => _control;
        set
        {
            if (_control == value)
            {
                return;
            }

            _control = value;
            OnPropertyChanged(nameof(Control));
            OnChanged();
        }
    }

    public bool Alt
    {
        get => _alt;
        set
        {
            if (_alt == value)
            {
                return;
            }

            _alt = value;
            OnPropertyChanged(nameof(Alt));
            OnChanged();
        }
    }

    public bool Shift
    {
        get => _shift;
        set
        {
            if (_shift == value)
            {
                return;
            }

            _shift = value;
            OnPropertyChanged(nameof(Shift));
            OnChanged();
        }
    }

    public ReadOnlyObservableCollection<MacroStepEditorDraft> Steps { get; }

    internal static MacroEditorDraft From(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MacroDefinition macro = settings.Macro;
        var steps = new List<MacroStepEditorDraft>();

        foreach (MacroStep step in macro.Sequence.Steps)
        {
            steps.Add(step switch
            {
                KeyTapStep keyTap => MacroStepEditorDraft.KeyTap(keyTap.Key),
                DelayStep delay => MacroStepEditorDraft.Delay(
                    FormatMilliseconds(delay.Duration)),
                _ => throw new InvalidOperationException(
                    $"Macro step type '{step.GetType().Name}' cannot be edited.")
            });
        }

        HotkeyModifiers modifiers = macro.Hotkey.Modifiers;

        return new MacroEditorDraft(
            macro.Name,
            macro.Hotkey.Key,
            (modifiers & HotkeyModifiers.Control) != 0,
            (modifiers & HotkeyModifiers.Alt) != 0,
            (modifiers & HotkeyModifiers.Shift) != 0,
            steps);
    }

    internal void AddKeyTap()
    {
        AddStep(MacroStepEditorDraft.KeyTap(KeyboardKey.A));
    }

    internal void AddDelay()
    {
        AddStep(MacroStepEditorDraft.Delay("150"));
    }

    internal void Remove(MacroStepEditorDraft step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (!_steps.Remove(step))
        {
            return;
        }

        step.Changed -= OnStepChanged;
        UpdateStepPositions();
        OnChanged();
    }

    internal void MoveUp(MacroStepEditorDraft step)
    {
        ArgumentNullException.ThrowIfNull(step);

        int index = _steps.IndexOf(step);
        if (index <= 0)
        {
            return;
        }

        _steps.Move(index, index - 1);
        UpdateStepPositions();
        OnChanged();
    }

    internal void MoveDown(MacroStepEditorDraft step)
    {
        ArgumentNullException.ThrowIfNull(step);

        int index = _steps.IndexOf(step);
        if (index < 0 || index >= _steps.Count - 1)
        {
            return;
        }

        _steps.Move(index, index + 1);
        UpdateStepPositions();
        OnChanged();
    }

    internal bool TryCreateCandidate(
        out ApplicationSettingsCandidate? candidate,
        out IReadOnlyList<EditorParseError> parseErrors)
    {
        var errors = new List<EditorParseError>();
        var steps = new List<MacroStep>(Steps.Count);

        for (int index = 0; index < Steps.Count; index++)
        {
            MacroStepEditorDraft step = Steps[index];

            if (step.Kind == MacroStepEditorKind.KeyTap)
            {
                steps.Add(new KeyTapStep(step.Key));
                continue;
            }

            if (!TryParseDelay(step.MillisecondsText, out TimeSpan duration))
            {
                errors.Add(new EditorParseError(
                    $"macro.steps[{index}].milliseconds",
                    $"Step {index + 1}: Delay must be a non-negative whole " +
                    "number of milliseconds."));
                continue;
            }

            steps.Add(new DelayStep(duration));
        }

        parseErrors = Array.AsReadOnly(errors.ToArray());

        if (errors.Count != 0)
        {
            candidate = null;
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if (Control)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Alt)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Shift)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        candidate = new ApplicationSettingsCandidate(
            new MacroDefinitionCandidate(
                Name,
                new KeyboardHotkey(HotkeyKey, modifiers),
                new MacroSequence(steps)));

        return true;
    }

    internal MacroEditorValidationResult Validate()
    {
        if (!TryCreateCandidate(
                out ApplicationSettingsCandidate? candidate,
                out IReadOnlyList<EditorParseError> parseErrors))
        {
            return MacroEditorValidationResult.InvalidParse(parseErrors);
        }

        ConfigurationValidationResult configurationResult =
            ConfigurationValidator.Validate(candidate!);

        return configurationResult.IsValid
            ? MacroEditorValidationResult.Valid(configurationResult.Settings!)
            : MacroEditorValidationResult.InvalidConfiguration(
                configurationResult.Errors);
    }

    private static bool TryParseDelay(string text, out TimeSpan duration)
    {
        duration = default;

        if (!long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long milliseconds) ||
            milliseconds < 0 ||
            milliseconds > MaximumTimeSpanMilliseconds)
        {
            return false;
        }

        duration = new TimeSpan(
            milliseconds * TimeSpan.TicksPerMillisecond);
        return true;
    }

    private static string FormatMilliseconds(TimeSpan duration)
    {
        decimal milliseconds =
            (decimal)duration.Ticks / TimeSpan.TicksPerMillisecond;

        return milliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private void AddStep(MacroStepEditorDraft step)
    {
        step.Changed += OnStepChanged;
        _steps.Add(step);
        UpdateStepPositions();
        OnChanged();
    }

    private void UpdateStepPositions()
    {
        for (int index = 0; index < Steps.Count; index++)
        {
            Steps[index].SetPosition(index + 1, Steps.Count);
        }
    }

    private void OnStepChanged(object? sender, EventArgs e)
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

internal enum MacroStepEditorKind
{
    KeyTap,
    Delay
}

internal sealed class MacroStepEditorDraft : INotifyPropertyChanged
{
    private KeyboardKey _key;
    private string _millisecondsText;
    private int _position;
    private bool _canMoveUp;
    private bool _canMoveDown;

    private MacroStepEditorDraft(
        MacroStepEditorKind kind,
        KeyboardKey key,
        string millisecondsText)
    {
        Kind = kind;
        _key = key;
        _millisecondsText = millisecondsText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? Changed;

    internal MacroStepEditorKind Kind { get; }

    public int Position => _position;

    public string TypeLabel =>
        Kind == MacroStepEditorKind.KeyTap ? "Key Tap" : "Delay";

    public bool IsKeyTap => Kind == MacroStepEditorKind.KeyTap;

    public bool IsDelay => Kind == MacroStepEditorKind.Delay;

    public bool CanMoveUp => _canMoveUp;

    public bool CanMoveDown => _canMoveDown;

    public KeyboardKey Key
    {
        get => _key;
        set
        {
            if (_key == value)
            {
                return;
            }

            _key = value;
            OnPropertyChanged(nameof(Key));
            OnChanged();
        }
    }

    public string MillisecondsText
    {
        get => _millisecondsText;
        set
        {
            if (_millisecondsText == value)
            {
                return;
            }

            _millisecondsText = value;
            OnPropertyChanged(nameof(MillisecondsText));
            OnChanged();
        }
    }

    internal static MacroStepEditorDraft KeyTap(KeyboardKey key)
    {
        return new MacroStepEditorDraft(
            MacroStepEditorKind.KeyTap,
            key,
            string.Empty);
    }

    internal static MacroStepEditorDraft Delay(string millisecondsText)
    {
        ArgumentNullException.ThrowIfNull(millisecondsText);

        return new MacroStepEditorDraft(
            MacroStepEditorKind.Delay,
            KeyboardKey.A,
            millisecondsText);
    }

    internal void SetPosition(int position, int stepCount)
    {
        bool canMoveUp = position > 1;
        bool canMoveDown = position < stepCount;

        if (_position != position)
        {
            _position = position;
            OnPropertyChanged(nameof(Position));
        }

        if (_canMoveUp != canMoveUp)
        {
            _canMoveUp = canMoveUp;
            OnPropertyChanged(nameof(CanMoveUp));
        }

        if (_canMoveDown != canMoveDown)
        {
            _canMoveDown = canMoveDown;
            OnPropertyChanged(nameof(CanMoveDown));
        }
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

internal sealed record EditorParseError(string Path, string Message);
