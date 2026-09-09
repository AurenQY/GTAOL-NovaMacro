using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

public partial class MainWindow : Window
{
    private readonly MacroEditorDraft _draft;

    public MainWindow()
    {
        HotkeyKeys = Array.AsReadOnly(Enum.GetValues<HotkeyKey>());
        OutputKeys = Array.AsReadOnly(Enum.GetValues<KeyboardKey>());

        InitializeComponent();

        _draft = MacroEditorDraft.From(StarterConfiguration.Default);
        _draft.Changed += OnDraftChanged;
        DataContext = _draft;

        RefreshValidation();
    }

    public IReadOnlyList<HotkeyKey> HotkeyKeys { get; }

    public IReadOnlyList<KeyboardKey> OutputKeys { get; }

    private void AddKeyTap_Click(object sender, RoutedEventArgs e)
    {
        _draft.AddKeyTap();
    }

    private void AddDelay_Click(object sender, RoutedEventArgs e)
    {
        _draft.AddDelay();
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (GetStepFromButton(sender) is { } step)
        {
            _draft.Remove(step);
        }
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e)
    {
        if (GetStepFromButton(sender) is { } step)
        {
            _draft.MoveUp(step);
        }
    }

    private void MoveStepDown_Click(object sender, RoutedEventArgs e)
    {
        if (GetStepFromButton(sender) is { } step)
        {
            _draft.MoveDown(step);
        }
    }

    private void OnDraftChanged(object? sender, EventArgs e)
    {
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        if (!_draft.TryCreateCandidate(
                out ApplicationSettingsCandidate? candidate,
                out IReadOnlyList<EditorParseError> parseErrors))
        {
            ShowValidationErrors(parseErrors.Select(error => error.Message));
            return;
        }

        ConfigurationValidationResult validationResult =
            ConfigurationValidator.Validate(candidate!);

        if (validationResult.IsValid)
        {
            ValidationStatusText.Text = "Configuration valid.";
            ValidationStatusText.Foreground = Brushes.ForestGreen;
            ValidationErrorsList.ItemsSource = Array.Empty<string>();
            ValidationErrorsList.Visibility = Visibility.Collapsed;
            return;
        }

        ShowValidationErrors(
            validationResult.Errors.Select(error => error.Message));
    }

    private void ShowValidationErrors(IEnumerable<string> messages)
    {
        ValidationStatusText.Text = "Configuration needs attention.";
        ValidationStatusText.Foreground = Brushes.Firebrick;
        ValidationErrorsList.ItemsSource = messages.ToArray();
        ValidationErrorsList.Visibility = Visibility.Visible;
    }

    private static MacroStepEditorDraft? GetStepFromButton(object sender)
    {
        return sender is Button { Tag: MacroStepEditorDraft step }
            ? step
            : null;
    }
}
