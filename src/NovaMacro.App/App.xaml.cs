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
    private MultiMacroHotkeyCoordinator? _coordinator;
    private SingleInstanceGuard? _instanceGuard;
    private ApplicationSettings? _runtimeSettings;
    private bool _isShuttingDown;

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
            var startupCoordinator = new StartupConfigurationCoordinator(settingsStore);
            StartupConfigurationResult startupConfiguration =
                startupCoordinator.Resolve();
            var saveCoordinator = new MacroEditorSaveCoordinator(settingsStore);
            var mainWindow = new MainWindow(saveCoordinator, startupConfiguration);
            MainWindow = mainWindow;

            if (startupConfiguration.RuntimeSettings is { } runtimeSettings)
            {
                ActivateRuntime(runtimeSettings, mainWindow);
            }

            mainWindow.Show();
        }
        catch (Exception exception)
        {
            DisposeRuntime();

            MessageBox.Show(
                "NovaMacro could not start.\n\n" + exception.Message,
                "NovaMacro startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void ActivateRuntime(
        ApplicationSettings runtimeSettings,
        MainWindow mainWindow)
    {
        var keyboardOutput = new WindowsKeyboardOutput();
        var mouseOutput = new WindowsMouseOutput();
        var delay = new TaskDelay();
        var macroRunner = new MacroRunner(keyboardOutput, mouseOutput, delay);
        var coordinator = new MultiMacroHotkeyCoordinator(
            runtimeSettings,
            () => new WindowsGlobalHotkeyService(),
            macroRunner,
            mouseButtonServiceFactory:
                () => new WindowsGlobalMouseButtonService());

        _runtimeSettings = runtimeSettings;
        coordinator.ExecutionFailed += OnRuntimeExecutionFailed;

        try
        {
            MultiMacroRuntimeStatus status = StartAndPresentInitialRuntimeStatus(
                coordinator,
                initialStatus => mainWindow.PresentInitialRuntimeStatus(
                    initialStatus,
                    runtimeSettings));
            _coordinator = coordinator;
            AttachRuntimeStatusUpdates(
                coordinator,
                status,
                OnRuntimeStatusChanged);
        }
        catch
        {
            coordinator.StatusChanged -= OnRuntimeStatusChanged;
            coordinator.ExecutionFailed -= OnRuntimeExecutionFailed;
            coordinator.Dispose();
            _runtimeSettings = null;
            throw;
        }
    }

    private void OnRuntimeStatusChanged(MultiMacroRuntimeStatus status)
    {
        if (_runtimeSettings is not { } runtimeSettings)
        {
            return;
        }

        DispatchToMainWindow(window =>
            window.PresentRuntimeStatusUpdate(status, runtimeSettings));
    }

    private void OnRuntimeExecutionFailed(MacroId id, Exception exception)
    {
        Trace.TraceError("NovaMacro runtime execution failed: {0}", exception);

        if (_runtimeSettings is not { } runtimeSettings)
        {
            return;
        }

        DispatchToMainWindow(window =>
            window.PresentExecutionFailure(id, exception, runtimeSettings));
    }

    private void DispatchToMainWindow(Action<MainWindow> update)
    {
        if (_isShuttingDown || Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_isShuttingDown && MainWindow is MainWindow window)
                {
                    update(window);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The dispatcher began shutting down after the guard above.
        }
    }

    internal static bool ShouldPresentCurrentStatusAsUpdate(
        MultiMacroRuntimeStatus initialStatus,
        MultiMacroRuntimeStatus? currentStatus)
    {
        ArgumentNullException.ThrowIfNull(initialStatus);

        return currentStatus is not null &&
            !ReferenceEquals(initialStatus, currentStatus);
    }

    internal static MultiMacroRuntimeStatus StartAndPresentInitialRuntimeStatus(
        MultiMacroHotkeyCoordinator coordinator,
        Action<MultiMacroRuntimeStatus> presentInitial)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(presentInitial);

        MultiMacroRuntimeStatus status = coordinator.Start();
        presentInitial(status);
        return status;
    }

    internal static void AttachRuntimeStatusUpdates(
        MultiMacroHotkeyCoordinator coordinator,
        MultiMacroRuntimeStatus initialStatus,
        Action<MultiMacroRuntimeStatus> presentUpdate)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(initialStatus);
        ArgumentNullException.ThrowIfNull(presentUpdate);

        coordinator.StatusChanged += presentUpdate;
        MultiMacroRuntimeStatus? currentStatus = coordinator.CurrentStatus;
        if (ShouldPresentCurrentStatusAsUpdate(initialStatus, currentStatus))
        {
            presentUpdate(currentStatus!);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;

        try
        {
            DisposeRuntime();
        }
        finally
        {
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
    }

    private void DisposeRuntime()
    {
        MultiMacroHotkeyCoordinator? coordinator = _coordinator;
        _coordinator = null;
        _runtimeSettings = null;

        if (coordinator is null)
        {
            return;
        }

        coordinator.StatusChanged -= OnRuntimeStatusChanged;
        coordinator.ExecutionFailed -= OnRuntimeExecutionFailed;

        try
        {
            coordinator.Dispose();
        }
        catch (Exception exception)
        {
            Trace.TraceError("NovaMacro runtime cleanup failed: {0}", exception);
        }
    }
}
