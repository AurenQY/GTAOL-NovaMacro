using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

public partial class MainWindow : Window
{
    private readonly MacroEditorSaveCoordinator _saveCoordinator;
    private readonly MacroEditorDraft _draft;

    internal MainWindow(
        MacroEditorSaveCoordinator saveCoordinator,
        ApplicationSettings initialSettings,
        RuntimeStartupStatus runtimeStatus)
    {
        ArgumentNullException.ThrowIfNull(saveCoordinator);
        ArgumentNullException.ThrowIfNull(initialSettings);
        ArgumentNullException.ThrowIfNull(runtimeStatus);

        _saveCoordinator = saveCoordinator;
        HotkeyKeys = Array.AsReadOnly(Enum.GetValues<HotkeyKey>());
        OutputKeys = Array.AsReadOnly(Enum.GetValues<KeyboardKey>());

        InitializeComponent();

        _draft = MacroEditorDraft.From(initialSettings);
        _draft.Changed += OnDraftChanged;
        DataContext = _draft;

        ShowRuntimeStatus(runtimeStatus);
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
        ClearSaveStatus();
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        ShowValidation(_saveCoordinator.Validate(_draft));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        MacroEditorSaveResult saveResult = _saveCoordinator.Save(_draft);

        ShowValidation(saveResult.Validation);

        switch (saveResult.Status)
        {
            case MacroEditorSaveStatus.Saved:
                ShowSaveStatus(
                    "Saved to disk. Changes take effect next time " +
                    "NovaMacro starts.\n" +
                    _saveCoordinator.SettingsPath,
                    Brushes.ForestGreen);
                break;

            case MacroEditorSaveStatus.Invalid:
                ClearSaveStatus();
                break;

            case MacroEditorSaveStatus.StorageFailed:
                SettingsStorageError error = saveResult.StorageError!;
                ShowSaveStatus(
                    $"Save failed: {error.Message}\n{error.Path}",
                    Brushes.Firebrick);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported save status '{saveResult.Status}'.");
        }
    }

    private void ShowRuntimeStatus(RuntimeStartupStatus runtimeStatus)
    {
        RuntimeStatusText.Text = runtimeStatus.Message;
        RuntimeStatusText.Foreground = runtimeStatus.IsActive
            ? Brushes.ForestGreen
            : Brushes.Firebrick;

        RuntimeDiagnosticsList.ItemsSource = runtimeStatus.Diagnostics;
        RuntimeDiagnosticsScrollViewer.Visibility =
            runtimeStatus.Diagnostics.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ShowValidation(MacroEditorValidationResult validationResult)
    {
        SaveButton.IsEnabled = validationResult.IsValid;

        if (validationResult.IsValid)
        {
            ValidationStatusText.Text = "Configuration valid.";
            ValidationStatusText.Foreground = Brushes.ForestGreen;
            ValidationErrorsList.ItemsSource = Array.Empty<string>();
            ValidationErrorsList.Visibility = Visibility.Collapsed;
            return;
        }

        ShowValidationErrors(
            validationResult.ErrorMessages);
    }

    private void ShowValidationErrors(IEnumerable<string> messages)
    {
        ValidationStatusText.Text = "Configuration needs attention.";
        ValidationStatusText.Foreground = Brushes.Firebrick;
        ValidationErrorsList.ItemsSource = messages.ToArray();
        ValidationErrorsList.Visibility = Visibility.Visible;
    }

    private void ShowSaveStatus(string message, Brush foreground)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = foreground;
        SaveStatusText.Visibility = Visibility.Visible;
    }

    private void ClearSaveStatus()
    {
        SaveStatusText.Text = string.Empty;
        SaveStatusText.Visibility = Visibility.Collapsed;
    }

    private static MacroStepEditorDraft? GetStepFromButton(object sender)
    {
        return sender is Button { Tag: MacroStepEditorDraft step }
            ? step
            : null;
    }
}
