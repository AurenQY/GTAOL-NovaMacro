using System.Runtime.ExceptionServices;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

internal sealed class MultiMacroHotkeyCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly object _notificationGate = new();
    private readonly MacroDefinition[] _macros;
    private readonly RuntimeMacroBinding[] _enabledBindings;
    private readonly Dictionary<MacroId, int> _macroIndexes;
    private readonly Dictionary<KeyboardHotkey, RuntimeMacroBinding>
        _bindingsByHotkey;
    private readonly Func<WindowsGlobalHotkeyService> _hotkeyServiceFactory;
    private readonly MacroRunner _macroRunner;
    private readonly Action? _terminalPhase1CompletedForTesting;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly List<GlobalHotkeyRegistration> _registrations = new();
    private readonly HashSet<ExecutionMarker> _executions = new();

    private CoordinatorState _state = CoordinatorState.Created;
    private WindowsGlobalHotkeyService? _hotkeyService;
    private MultiMacroRuntimeStatus? _currentStatus;
    private bool _subscribed;
    private bool _executionAllowed;
    private bool _notificationsAllowed = true;

    internal MultiMacroHotkeyCoordinator(
        ApplicationSettings settings,
        Func<WindowsGlobalHotkeyService> hotkeyServiceFactory,
        MacroRunner macroRunner,
        Action? terminalPhase1CompletedForTesting = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeyServiceFactory);
        ArgumentNullException.ThrowIfNull(macroRunner);

        _macros = settings.Macros.ToArray();
        _hotkeyServiceFactory = hotkeyServiceFactory;
        _macroRunner = macroRunner;
        _terminalPhase1CompletedForTesting =
            terminalPhase1CompletedForTesting;
        _macroIndexes = new Dictionary<MacroId, int>(_macros.Length);
        _bindingsByHotkey = new Dictionary<KeyboardHotkey, RuntimeMacroBinding>();

        var enabledBindings = new List<RuntimeMacroBinding>();

        for (int index = 0; index < _macros.Length; index++)
        {
            MacroDefinition macro = _macros[index];

            if (!_macroIndexes.TryAdd(macro.Id, index))
            {
                throw new InvalidOperationException(
                    $"Runtime settings contain duplicate macro ID '{macro.Id}'.");
            }

            if (!macro.Enabled)
            {
                continue;
            }

            var binding = new RuntimeMacroBinding(
                macro.Id,
                macro.Hotkey,
                macro.Sequence);

            if (!_bindingsByHotkey.TryAdd(binding.Hotkey, binding))
            {
                throw new InvalidOperationException(
                    $"Runtime settings contain duplicate enabled hotkey " +
                    $"'{binding.Hotkey}'.");
            }

            enabledBindings.Add(binding);
        }

        _enabledBindings = enabledBindings.ToArray();
    }

    internal event Action<MultiMacroRuntimeStatus>? StatusChanged;

    internal event Action<MacroId, Exception>? ExecutionFailed;

    internal MultiMacroRuntimeStatus? CurrentStatus
    {
        get
        {
            lock (_gate)
            {
                return _currentStatus;
            }
        }
    }

    internal MultiMacroRuntimeStatus Start()
    {
        lock (_notificationGate)
        {
            lock (_gate)
            {
                if (_state != CoordinatorState.Created)
                {
                    throw new InvalidOperationException(
                        "The multi-macro hotkey coordinator cannot be started again.");
                }

                _state = CoordinatorState.Activating;
            }

            MultiMacroRuntimeStatus snapshot;

            if (_enabledBindings.Length == 0)
            {
                lock (_gate)
                {
                    snapshot = SetInitialStatus(
                        MultiMacroRuntimeState.NoEnabledMacros,
                        CreateDisabledStatuses());
                    _state = CoordinatorState.Active;
                    _executionAllowed = true;
                }
            }
            else
            {
                snapshot = ActivateEnabledBindings();
            }

            PublishStatusChanged(snapshot);
            return snapshot;
        }
    }

    public void Dispose()
    {
        GlobalHotkeyRegistration[] registrations;
        WindowsGlobalHotkeyService? hotkeyService;
        Task[] executionTasks;

        lock (_notificationGate)
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
                        "The multi-macro hotkey coordinator is already being disposed.");
                }

                _notificationsAllowed = false;
                _executionAllowed = false;
                _state = CoordinatorState.Disposing;
                UnsubscribeFromHotkeyService();
                registrations = _registrations.ToArray();
                _registrations.Clear();
                hotkeyService = _hotkeyService;
                _hotkeyService = null;
                executionTasks = _executions
                    .Select(execution => execution.Completion.Task)
                    .ToArray();
            }
        }

        var failures = new List<Exception>();

        try
        {
            Exception? cancellationFailure = RequestExecutionCancellation();
            if (cancellationFailure is not null)
            {
                failures.Add(cancellationFailure);
            }

            DisposeRegistrations(registrations, failures);

            if (hotkeyService is not null)
            {
                TryDispose(hotkeyService, failures);
            }

            try
            {
                Task.WhenAll(executionTasks).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        finally
        {
            _lifetimeCancellation.Dispose();

            lock (_gate)
            {
                _executions.Clear();
                _state = CoordinatorState.Disposed;
            }
        }

        ThrowDisposalFailures(failures);
    }

    private MultiMacroRuntimeStatus ActivateEnabledBindings()
    {
        RuntimeMacroStatus[] statuses = CreatePendingEnabledStatuses();

        try
        {
            WindowsGlobalHotkeyService hotkeyService =
                _hotkeyServiceFactory() ??
                throw new InvalidOperationException(
                    "The global hotkey service factory returned null.");

            lock (_gate)
            {
                _hotkeyService = hotkeyService;
                SubscribeToHotkeyService(hotkeyService);
            }

            _ = ObserveMessageLoopCompletionAsync(hotkeyService);

            foreach (RuntimeMacroBinding binding in _enabledBindings)
            {
                try
                {
                    GlobalHotkeyRegistration registration =
                        hotkeyService.Register(binding.Hotkey);

                    lock (_gate)
                    {
                        _registrations.Add(registration);
                    }

                    statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                        binding.Id,
                        RuntimeMacroState.Active);
                }
                catch (GlobalHotkeyRegistrationException exception)
                {
                    statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                        binding.Id,
                        RuntimeMacroState.RegistrationFailed,
                        exception.Message);
                }
            }

            MultiMacroRuntimeState aggregateState =
                DetermineAggregateState(statuses);

            lock (_gate)
            {
                _state = CoordinatorState.Active;
                _executionAllowed = true;
                return SetInitialStatus(aggregateState, statuses);
            }
        }
        catch (Exception exception)
        {
            return CompleteFailedActivation(exception);
        }
    }

    private MultiMacroRuntimeStatus CompleteFailedActivation(
        Exception activationFailure)
    {
        var failures = new List<Exception> { activationFailure };
        GlobalHotkeyRegistration[] registrations;
        WindowsGlobalHotkeyService? hotkeyService;

        lock (_gate)
        {
            _executionAllowed = false;
            _state = CoordinatorState.ServiceUnavailableTransitioning;
            UnsubscribeFromHotkeyService();
            registrations = _registrations.ToArray();
            _registrations.Clear();
            hotkeyService = _hotkeyService;
            _hotkeyService = null;
        }

        Exception? cancellationFailure = RequestExecutionCancellation();
        if (cancellationFailure is not null)
        {
            failures.Add(cancellationFailure);
        }

        DisposeRegistrations(registrations, failures);

        if (hotkeyService is not null)
        {
            TryDispose(hotkeyService, failures);
        }

        Exception failure = failures.Count == 1
            ? activationFailure
            : new AggregateException(
                "Runtime activation and cleanup failed.",
                failures);

        lock (_gate)
        {
            _state = CoordinatorState.ServiceUnavailable;
            return SetInitialStatus(
                MultiMacroRuntimeState.ServiceUnavailable,
                CreateUnavailableStatuses(failure.Message));
        }
    }

    private async Task ObserveMessageLoopCompletionAsync(
        WindowsGlobalHotkeyService hotkeyService)
    {
        // Never run terminal transition code inline while Start owns its locks.
        await Task.Yield();

        Exception? failure;

        try
        {
            failure = await hotkeyService.MessageLoopCompletion
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is null)
        {
            return;
        }

        lock (_notificationGate)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_hotkeyService, hotkeyService) ||
                    _state is CoordinatorState.ServiceUnavailable or
                        CoordinatorState.ServiceUnavailableTransitioning or
                        CoordinatorState.Disposing or
                        CoordinatorState.Disposed)
                {
                    return;
                }

                UnsubscribeFromHotkeyService();
                _registrations.Clear();
                _executionAllowed = false;
                _state = CoordinatorState.ServiceUnavailableTransitioning;
            }

            _terminalPhase1CompletedForTesting?.Invoke();
            _ = RequestExecutionCancellation();

            MultiMacroRuntimeStatus snapshot;

            lock (_gate)
            {
                if (_state != CoordinatorState.ServiceUnavailableTransitioning)
                {
                    return;
                }

                _state = CoordinatorState.ServiceUnavailable;
                snapshot = new MultiMacroRuntimeStatus(
                    MultiMacroRuntimeState.ServiceUnavailable,
                    CreateUnavailableStatuses(failure.Message));
                _currentStatus = snapshot;
            }

            PublishStatusChanged(snapshot);
        }
    }

    private void OnTriggered(KeyboardHotkey hotkey)
    {
        ExecutionMarker execution;

        lock (_gate)
        {
            if (!_executionAllowed ||
                _state != CoordinatorState.Active ||
                !_bindingsByHotkey.TryGetValue(
                    hotkey,
                    out RuntimeMacroBinding? candidate))
            {
                return;
            }

            execution = new ExecutionMarker(
                candidate,
                _lifetimeCancellation.Token);
            _executions.Add(execution);
        }

        Task<Exception?> executionTask = RunMacroAsync(execution);
        _ = executionTask.ContinueWith(
            completedTask => OnMacroTaskCompleted(
                execution,
                completedTask),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnTriggerFailed(
        KeyboardHotkey hotkey,
        Exception exception)
    {
        RuntimeMacroBinding? binding;

        lock (_gate)
        {
            if (_state != CoordinatorState.Active ||
                !_bindingsByHotkey.TryGetValue(hotkey, out binding))
            {
                return;
            }
        }

        PublishExecutionFailed(binding.Id, exception);
    }

    private async Task<Exception?> RunMacroAsync(ExecutionMarker execution)
    {
        await Task.Yield();

        try
        {
            lock (_gate)
            {
                if (!_executionAllowed ||
                    _state != CoordinatorState.Active ||
                    execution.State != ExecutionState.PendingStart)
                {
                    return null;
                }

                execution.State = ExecutionState.Started;
            }

            MacroRunResult result = await _macroRunner
                .RunAsync(
                    execution.Binding.Sequence,
                    execution.CancellationToken)
                .ConfigureAwait(false);

            if (result == MacroRunResult.AlreadyRunning)
            {
                return null;
            }

            return null;
        }
        catch (OperationCanceledException)
            when (execution.CancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            return execution.CancellationToken.IsCancellationRequested
                ? null
                : exception;
        }
    }

    private void OnMacroTaskCompleted(
        ExecutionMarker execution,
        Task<Exception?> completedTask)
    {
        Exception? failure;

        try
        {
            failure = completedTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (_gate)
        {
            execution.State = ExecutionState.Completed;
            _executions.Remove(execution);
        }

        execution.Completion.TrySetResult(failure);

        if (failure is not null)
        {
            PublishExecutionFailed(execution.Binding.Id, failure);
        }
    }

    private void SubscribeToHotkeyService(
        WindowsGlobalHotkeyService hotkeyService)
    {
        hotkeyService.Triggered += OnTriggered;
        hotkeyService.TriggerFailed += OnTriggerFailed;
        _subscribed = true;
    }

    private void UnsubscribeFromHotkeyService()
    {
        if (!_subscribed || _hotkeyService is null)
        {
            return;
        }

        _hotkeyService.Triggered -= OnTriggered;
        _hotkeyService.TriggerFailed -= OnTriggerFailed;
        _subscribed = false;
    }

    private RuntimeMacroStatus[] CreateDisabledStatuses()
    {
        return _macros
            .Select(macro => new RuntimeMacroStatus(
                macro.Id,
                RuntimeMacroState.Disabled))
            .ToArray();
    }

    private RuntimeMacroStatus[] CreatePendingEnabledStatuses()
    {
        return _macros
            .Select(macro => new RuntimeMacroStatus(
                macro.Id,
                macro.Enabled
                    ? RuntimeMacroState.RuntimeUnavailable
                    : RuntimeMacroState.Disabled))
            .ToArray();
    }

    private RuntimeMacroStatus[] CreateUnavailableStatuses(string diagnostic)
    {
        return _macros
            .Select(macro => new RuntimeMacroStatus(
                macro.Id,
                macro.Enabled
                    ? RuntimeMacroState.RuntimeUnavailable
                    : RuntimeMacroState.Disabled,
                macro.Enabled ? diagnostic : null))
            .ToArray();
    }

    private MultiMacroRuntimeStatus SetInitialStatus(
        MultiMacroRuntimeState state,
        IEnumerable<RuntimeMacroStatus> statuses)
    {
        var snapshot = new MultiMacroRuntimeStatus(state, statuses);
        _currentStatus = snapshot;
        return snapshot;
    }

    private static MultiMacroRuntimeState DetermineAggregateState(
        IEnumerable<RuntimeMacroStatus> statuses)
    {
        RuntimeMacroStatus[] enabledStatuses = statuses
            .Where(status => status.State != RuntimeMacroState.Disabled)
            .ToArray();
        int activeCount = enabledStatuses.Count(
            status => status.State == RuntimeMacroState.Active);

        if (activeCount == enabledStatuses.Length)
        {
            return MultiMacroRuntimeState.Active;
        }

        return activeCount > 0
            ? MultiMacroRuntimeState.PartiallyActive
            : MultiMacroRuntimeState.NoRegistrationsActive;
    }

    private void PublishStatusChanged(MultiMacroRuntimeStatus status)
    {
        Action<MultiMacroRuntimeStatus>? subscribers = StatusChanged;
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            // Dispose uses this same gate to close notification admission.
            lock (_notificationGate)
            {
                if (!_notificationsAllowed)
                {
                    return;
                }

                try
                {
                    ((Action<MultiMacroRuntimeStatus>)subscriber)(status);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private void PublishExecutionFailed(MacroId id, Exception exception)
    {
        Action<MacroId, Exception>? subscribers = ExecutionFailed;
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            lock (_notificationGate)
            {
                lock (_gate)
                {
                    if (!_notificationsAllowed ||
                        !_executionAllowed ||
                        _state != CoordinatorState.Active)
                    {
                        return;
                    }
                }

                try
                {
                    ((Action<MacroId, Exception>)subscriber)(id, exception);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private static void DisposeRegistrations(
        IEnumerable<GlobalHotkeyRegistration> registrations,
        ICollection<Exception> failures)
    {
        foreach (GlobalHotkeyRegistration registration in registrations)
        {
            TryDispose(registration, failures);
        }
    }

    private Exception? RequestExecutionCancellation()
    {
        try
        {
            _lifetimeCancellation.Cancel();
            return null;
        }
        catch (Exception exception)
        {
            // Cancellation is already requested even if a token callback fails.
            return exception;
        }
    }

    private static void TryDispose(
        IDisposable disposable,
        ICollection<Exception> failures)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowDisposalFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
            return;
        }

        throw new AggregateException(
            "One or more multi-macro runtime cleanup operations failed.",
            failures);
    }

    private enum CoordinatorState
    {
        Created,
        Activating,
        Active,
        ServiceUnavailableTransitioning,
        ServiceUnavailable,
        Disposing,
        Disposed
    }

    private enum ExecutionState
    {
        PendingStart,
        Started,
        Completed
    }

    private sealed class ExecutionMarker
    {
        internal ExecutionMarker(
            RuntimeMacroBinding binding,
            CancellationToken cancellationToken)
        {
            Binding = binding;
            CancellationToken = cancellationToken;
        }

        internal RuntimeMacroBinding Binding { get; }

        internal CancellationToken CancellationToken { get; }

        internal TaskCompletionSource<Exception?> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal ExecutionState State { get; set; } =
            ExecutionState.PendingStart;
    }
}
