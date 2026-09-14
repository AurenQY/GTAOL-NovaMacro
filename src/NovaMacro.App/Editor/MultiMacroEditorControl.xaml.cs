using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Editor;

public partial class MultiMacroEditorControl : UserControl
{
    private readonly MultiMacroEditorDraft _draft;

    public MultiMacroEditorControl()
        : this(new MultiMacroEditorDraft())
    {
    }

    internal MultiMacroEditorControl(MultiMacroEditorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;
        HotkeyKeys = Array.AsReadOnly(Enum.GetValues<HotkeyKey>());
        OutputKeys = Array.AsReadOnly(Enum.GetValues<KeyboardKey>());
        TriggerKinds = Array.AsReadOnly(Enum.GetValues<MacroTriggerEditorKind>());
        MouseTriggerButtons = Array.AsReadOnly(new[]
        {
            MouseButton.XButton1,
            MouseButton.XButton2
        });
        ExecutionModes = Array.AsReadOnly(
            Enum.GetValues<MacroExecutionMode>());

        InitializeComponent();

        _draft.Changed += OnDraftChanged;
        DataContext = _draft;
        RefreshValidation();
    }

    public IReadOnlyList<HotkeyKey> HotkeyKeys { get; }

    public IReadOnlyList<KeyboardKey> OutputKeys { get; }

    public IReadOnlyList<MacroTriggerEditorKind> TriggerKinds { get; }

    public IReadOnlyList<MouseButton> MouseTriggerButtons { get; }

    public IReadOnlyList<MacroExecutionMode> ExecutionModes { get; }

    private void AddMacro_Click(object sender, RoutedEventArgs e)
    {
        _draft.AddMacro();
    }

    private void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        _draft.DeleteSelectedMacro();
    }

    private void AddKeyTap_Click(object sender, RoutedEventArgs e)
    {
        _draft.SelectedMacro?.AddKeyTap();
    }

    private void AddDelay_Click(object sender, RoutedEventArgs e)
    {
        _draft.SelectedMacro?.AddDelay();
    }

    private void AddMouseTap_Click(object sender, RoutedEventArgs e)
    {
        _draft.SelectedMacro?.AddMouseButtonTap();
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (GetStepFromButton(sender) is { } step)
        {
            _draft.SelectedMacro?.Remove(step);
        }
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e)
    {
        if (GetStepFromButton(sender) is { } step)
        {
            _draft.SelectedMacro?.MoveUp(step);
        }
    }

    private void MoveStepDown_Click(object sender, RoutedEventArgs e)
    {
        if (GetStepFromButton(sender) is { } step)
        {
            _draft.SelectedMacro?.MoveDown(step);
        }
    }

    private void OnDraftChanged(object? sender, EventArgs e)
    {
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        MacroEditorValidationResult validation = _draft.Validate();
        DeleteMacroButton.IsEnabled = _draft.SelectedMacro is not null;

        if (validation.IsValid)
        {
            ValidationPanel.Visibility = Visibility.Collapsed;
            ValidationStatusText.Foreground = Brushes.DarkGreen;
            ValidationStatusText.Text = "All macros are valid.";
            ValidationErrorsList.ItemsSource = Array.Empty<string>();
            return;
        }

        ValidationPanel.Visibility = Visibility.Visible;
        ValidationStatusText.Foreground = Brushes.Firebrick;
        ValidationStatusText.Text = "Fix the listed issues before saving after integration.";
        ValidationErrorsList.ItemsSource = FormatValidationMessages(validation);
    }

    internal static IReadOnlyList<string> FormatValidationMessages(
        MacroEditorValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return validation.ParseErrors
            .Select(error => FormatValidationMessage(error.Path, error.Message))
            .Concat(validation.ConfigurationErrors.Select(
                error => FormatValidationMessage(error.Path, error.Message)))
            .ToArray();
    }

    internal static string FormatValidationMessage(string? path, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}";
    }

    private static MacroStepEditorDraft? GetStepFromButton(object sender)
    {
        return sender is Button { Tag: MacroStepEditorDraft step } ? step : null;
    }
}
