using System.Runtime.ExceptionServices;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

internal sealed class MacroHotkeyCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _macroTasks = new();
    private readonly WindowsGlobalHotkeyService _hotkeyService;
    private readonly MacroRunner _macroRunner;
    private readonly MacroSequence _sequence;
    private readonly Action<Exception> _reportRuntimeFailure;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private CoordinatorState _state = CoordinatorState.Created;

    internal MacroHotkeyCoordinator(
        WindowsGlobalHotkeyService hotkeyService,
        MacroRunner macroRunner,
        MacroSequence sequence,
        Action<Exception> reportRuntimeFailure)
    {
        ArgumentNullException.ThrowIfNull(hotkeyService);
        ArgumentNullException.ThrowIfNull(macroRunner);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(reportRuntimeFailure);

        _hotkeyService = hotkeyService;
        _macroRunner = macroRunner;
        _sequence = sequence;
        _reportRuntimeFailure = reportRuntimeFailure;
    }

    internal void Start(KeyboardHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        lock (_gate)
        {
            if (_state != CoordinatorState.Created)
            {
                throw new InvalidOperationException(
                    "The macro hotkey coordinator cannot be started again.");
            }

            _hotkeyService.Triggered += OnTriggered;
            _hotkeyService.TriggerFailed += OnTriggerFailed;

            try
            {
                _hotkeyService.Register(hotkey);
                _state = CoordinatorState.Active;
            }
            catch
            {
                _hotkeyService.Triggered -= OnTriggered;
                _hotkeyService.TriggerFailed -= OnTriggerFailed;
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == CoordinatorState.Disposed)
            {
                return;
            }

            if (_state == CoordinatorState.Disposing)
            {
                throw new InvalidOperationException(
                    "The macro hotkey coordinator is already being disposed.");
            }

            _state = CoordinatorState.Disposing;
            _hotkeyService.Triggered -= OnTriggered;
            _hotkeyService.TriggerFailed -= OnTriggerFailed;
        }

        Exception? disposalFailure = null;

        try
        {
            _lifetimeCancellation.Cancel();

            try
            {
                _hotkeyService.Dispose();
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }

            Task[] tasks;
            lock (_gate)
            {
                tasks = _macroTasks.ToArray();
            }

            try
            {
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                ReportRuntimeFailureSafely(exception);
            }
        }
        finally
        {
            _lifetimeCancellation.Dispose();

            lock (_gate)
            {
                _macroTasks.Clear();
                _state = CoordinatorState.Disposed;
            }
        }

        if (disposalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(disposalFailure).Throw();
        }
    }

    private void OnTriggered(KeyboardHotkey hotkey)
    {
        Task task;

        lock (_gate)
        {
            if (_state != CoordinatorState.Active)
            {
                return;
            }

            task = RunMacroSafelyAsync();
            _macroTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask => OnMacroTaskCompleted(completedTask),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnTriggerFailed(
        KeyboardHotkey hotkey,
        Exception exception)
    {
        lock (_gate)
        {
            if (_state != CoordinatorState.Active)
            {
                return;
            }
        }

        ReportRuntimeFailureSafely(exception);
    }

    private async Task RunMacroSafelyAsync()
    {
        try
        {
            MacroRunResult result = await _macroRunner
                .RunAsync(_sequence, _lifetimeCancellation.Token)
                .ConfigureAwait(false);

            if (result == MacroRunResult.AlreadyRunning)
            {
                return;
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportRuntimeFailureSafely(exception);
        }
    }

    private void OnMacroTaskCompleted(Task completedTask)
    {
        _ = completedTask.Exception;

        lock (_gate)
        {
            _macroTasks.Remove(completedTask);
        }
    }

    private void ReportRuntimeFailureSafely(Exception exception)
    {
        try
        {
            _reportRuntimeFailure(exception);
        }
        catch (Exception)
        {
        }
    }

    private enum CoordinatorState
    {
        Created,
        Active,
        Disposing,
        Disposed
    }
}
