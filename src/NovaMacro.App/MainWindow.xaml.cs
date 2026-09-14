using System.Windows;
using System.Windows.Media;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;

namespace NovaMacro.App;

public partial class MainWindow : Window
{
    private readonly MacroEditorSaveCoordinator _saveCoordinator;
    private readonly MultiMacroEditorDraft _draft;
    private readonly bool _requiresRecoveryConfirmation;
    private bool _recoverySaveConfirmed;

    internal MainWindow(
        MacroEditorSaveCoordinator saveCoordinator,
        StartupConfigurationResult startupConfiguration)
    {
        ArgumentNullException.ThrowIfNull(saveCoordinator);
        ArgumentNullException.ThrowIfNull(startupConfiguration);

        _saveCoordinator = saveCoordinator;
        _draft = MultiMacroEditorDraft.From(
            startupConfiguration.InitialEditorSettings);
        _requiresRecoveryConfirmation = startupConfiguration.Status is
            StartupConfigurationStatus.InvalidRecovery or
            StartupConfigurationStatus.UnavailableRecovery;

        InitializeComponent();

        MultiEditorHost.Content = new MultiMacroEditorControl(_draft);
        _draft.Changed += OnDraftChanged;
        ShowStartupStatus(startupConfiguration);
        RefreshSaveAvailability();
    }

    internal MultiMacroEditorDraft Draft => _draft;

    internal void PresentInitialRuntimeStatus(
        MultiMacroRuntimeStatus status,
        ApplicationSettings startupSettings)
    {
        ShowRuntimeStatus(RuntimeStatusPresentation.FromInitial(
            status,
            startupSettings));
    }

    internal void PresentRuntimeStatusUpdate(
        MultiMacroRuntimeStatus status,
        ApplicationSettings startupSettings)
    {
        ShowRuntimeStatus(RuntimeStatusPresentation.FromUpdate(
            status,
            startupSettings));
    }

    internal void PresentExecutionFailure(
        MacroId id,
        Exception exception,
        ApplicationSettings startupSettings)
    {
        string message = RuntimeStatusPresentation.FormatExecutionFailure(
            id,
            exception,
            startupSettings);
        RuntimeStatusText.Text = "Runtime execution failed. The configuration was not changed.";
        RuntimeStatusText.Foreground = Brushes.Firebrick;
        SetRuntimePanelAppearance(compact: false);
        RuntimeDiagnosticsList.ItemsSource = new[] { message };
        RuntimeDiagnosticsScrollViewer.Visibility = Visibility.Visible;
    }

    private void OnDraftChanged(object? sender, EventArgs e)
    {
        ClearSaveStatus();
        RefreshSaveAvailability();
    }

    private void RefreshSaveAvailability()
    {
        SaveButton.IsEnabled = _saveCoordinator.Validate(_draft).IsValid;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_saveCoordinator.Validate(_draft).IsValid)
        {
            RefreshSaveAvailability();
            return;
        }

        if (_requiresRecoveryConfirmation && !_recoverySaveConfirmed)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                "This will replace the settings file that could not be loaded with the current macro configuration. Continue?",
                "Replace settings file?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            _recoverySaveConfirmed = true;
        }

        MacroEditorSaveResult saveResult = _saveCoordinator.Save(_draft);

        switch (saveResult.Status)
        {
            case MacroEditorSaveStatus.Saved:
                ShowSaveStatus(
                    "Saved to disk. Changes take effect after restarting NovaMacro.\n" +
                    _saveCoordinator.SettingsPath,
                    Brushes.ForestGreen);
                break;

            case MacroEditorSaveStatus.Invalid:
                ClearSaveStatus();
                RefreshSaveAvailability();
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

    private void ShowStartupStatus(StartupConfigurationResult startupConfiguration)
    {
        if (startupConfiguration.RuntimeSettings is not null)
        {
            RuntimeStatusText.Text = "Runtime: Preparing";
            RuntimeStatusText.Foreground = Brushes.DimGray;
            SetRuntimePanelAppearance(compact: true);
            RuntimeDiagnosticsScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        RuntimeStatusText.Text = startupConfiguration.Status ==
            StartupConfigurationStatus.InvalidRecovery
            ? "Runtime inactive: saved configuration could not be loaded."
            : "Runtime inactive: settings storage is unavailable.";
        RuntimeStatusText.Foreground = Brushes.Firebrick;
        SetRuntimePanelAppearance(compact: false);
        RuntimeDiagnosticsList.ItemsSource = startupConfiguration.Diagnostics;
        RuntimeDiagnosticsScrollViewer.Visibility = Visibility.Visible;
    }

    private void ShowRuntimeStatus(RuntimeStatusPresentation presentation)
    {
        RuntimeStatusText.Text = presentation.Message;
        RuntimeStatusText.Foreground = presentation.IsHealthy
            ? Brushes.ForestGreen
            : Brushes.Firebrick;
        SetRuntimePanelAppearance(presentation.IsHealthy &&
            presentation.Diagnostics.Count == 0);
        RuntimeDiagnosticsList.ItemsSource = presentation.Diagnostics;
        RuntimeDiagnosticsScrollViewer.Visibility = presentation.Diagnostics.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetRuntimePanelAppearance(bool compact)
    {
        RuntimeStatusPanel.Padding = compact
            ? new Thickness(0)
            : new Thickness(10);
        RuntimeStatusPanel.Background = compact
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(247, 247, 247));
        RuntimeStatusPanel.BorderBrush = compact
            ? Brushes.Transparent
            : Brushes.LightGray;
        RuntimeStatusPanel.BorderThickness = compact
            ? new Thickness(0)
            : new Thickness(1);
        RuntimeStatusPanel.CornerRadius = compact
            ? new CornerRadius(0)
            : new CornerRadius(4);
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
}
