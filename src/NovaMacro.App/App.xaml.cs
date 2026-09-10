using System.Diagnostics;
using System.Windows;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

public partial class App : Application
{
    private MacroHotkeyCoordinator? _coordinator;
    private SingleInstanceGuard? _instanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstanceGuard.TryAcquire(
                out SingleInstanceGuard? instanceGuard))
        {
            MessageBox.Show(
                "NovaMacro is already running.",
                "NovaMacro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown(-1);
            return;
        }

        _instanceGuard = instanceGuard;

        try
        {
            var settingsStore = new JsonSettingsStore();
            var startupCoordinator =
                new StartupConfigurationCoordinator(settingsStore);
            StartupConfigurationResult startupConfiguration =
                startupCoordinator.Resolve();

            var editorSaveCoordinator =
                new MacroEditorSaveCoordinator(settingsStore);

            RuntimeStartupStatus runtimeStatus =
                ResolveRuntimeStatus(startupConfiguration);

            var mainWindow = new MainWindow(
                editorSaveCoordinator,
                startupConfiguration.InitialEditorSettings,
                runtimeStatus);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MacroHotkeyCoordinator? coordinatorToDispose = _coordinator;
            _coordinator = null;

            DisposeAfterStartupFailure(coordinatorToDispose, null);

            MessageBox.Show(
                "NovaMacro could not start.\n\n" +
                exception.Message,
                "NovaMacro startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    private RuntimeStartupStatus ResolveRuntimeStatus(
        StartupConfigurationResult startupConfiguration)
    {
        ApplicationSettings? runtimeSettings =
            startupConfiguration.RuntimeSettings;

        if (runtimeSettings is null)
        {
            return RuntimeStartupStatus.Recovery(startupConfiguration);
        }

        if (TryActivateRuntime(runtimeSettings, out Exception? error))
        {
            return RuntimeStartupStatus.Active(startupConfiguration);
        }

        return RuntimeStartupStatus.ActivationFailed(
            runtimeSettings.Macro.Hotkey,
            error!);
    }

    private bool TryActivateRuntime(
        ApplicationSettings runtimeSettings,
        out Exception? activationError)
    {
        WindowsGlobalHotkeyService? hotkeyService = null;
        MacroHotkeyCoordinator? coordinator = null;

        try
        {
            var keyboardOutput = new WindowsKeyboardOutput();
            var delay = new TaskDelay();
            var macroRunner = new MacroRunner(keyboardOutput, delay);

            hotkeyService = new WindowsGlobalHotkeyService();
            coordinator = new MacroHotkeyCoordinator(
                hotkeyService,
                macroRunner,
                runtimeSettings.Macro.Sequence,
                ReportRuntimeFailure);

            // The coordinator owns the hotkey service after construction.
            hotkeyService = null;

            coordinator.Start(runtimeSettings.Macro.Hotkey);
            _coordinator = coordinator;
            coordinator = null;
            activationError = null;
            return true;
        }
        catch (Exception exception)
        {
            _coordinator = null;
            DisposeAfterStartupFailure(coordinator, hotkeyService);
            activationError = exception;
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _coordinator?.Dispose();
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "NovaMacro shutdown cleanup failed: {0}",
                exception);
        }
        finally
        {
            _coordinator = null;
        }

        try
        {
            base.OnExit(e);
        }
        finally
        {
            try
            {
                _instanceGuard?.Dispose();
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "NovaMacro single-instance cleanup failed: {0}",
                    exception);
            }
            finally
            {
                _instanceGuard = null;
            }
        }
    }

    private static void DisposeAfterStartupFailure(
        MacroHotkeyCoordinator? coordinator,
        WindowsGlobalHotkeyService? hotkeyService)
    {
        try
        {
            if (coordinator is not null)
            {
                coordinator.Dispose();
            }
            else
            {
                hotkeyService?.Dispose();
            }
        }
        catch (Exception cleanupException)
        {
            Trace.TraceError(
                "NovaMacro startup cleanup failed: {0}",
                cleanupException);
        }
    }

    private static void ReportRuntimeFailure(Exception exception)
    {
        Trace.TraceError("NovaMacro runtime failure: {0}", exception);
    }
}
