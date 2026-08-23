using System.Diagnostics;
using System.Windows;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

public partial class App : Application
{
    private MacroHotkeyCoordinator? _coordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
                BuiltInMacroSequences.AbcTest,
                ReportRuntimeFailure);

            // The coordinator owns the hotkey service after construction.
            hotkeyService = null;

            coordinator.Start(new KeyboardHotkey(HotkeyKey.F8));
            _coordinator = coordinator;
            coordinator = null;

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MacroHotkeyCoordinator? coordinatorToDispose =
                coordinator ?? _coordinator;
            _coordinator = null;

            DisposeAfterStartupFailure(coordinatorToDispose, hotkeyService);

            MessageBox.Show(
                "NovaMacro could not start the F8 technical hotkey.\n\n" +
                exception.Message,
                "NovaMacro startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
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
            base.OnExit(e);
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
