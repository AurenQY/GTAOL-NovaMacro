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
    private readonly Dictionary<MouseButton, RuntimeMacroBinding>
        _bindingsByMouseButton;
    private readonly Func<WindowsGlobalHotkeyService> _hotkeyServiceFactory;
    private readonly Func<WindowsGlobalMouseButtonService>
        _mouseButtonServiceFactory;
    private readonly MacroRunner _macroRunner;
    private readonly Action? _terminalPhase1CompletedForTesting;
    private readonly Action? _executionOwnerReleasedForTesting;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource _activationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<GlobalHotkeyRegistration> _registrations = new();
    private readonly List<GlobalMouseButtonRegistration>
        _mouseRegistrations = new();
    private readonly HashSet<ExecutionMarker> _executions = new();

    private CoordinatorState _state = CoordinatorState.Created;
    private WindowsGlobalHotkeyService? _hotkeyService;
    private WindowsGlobalMouseButtonService? _mouseButtonService;
    private MultiMacroRuntimeStatus? _currentStatus;
    private bool _subscribed;
    private bool _mouseSubscribed;
    private bool _executionAllowed;
    private bool _notificationsAllowed = true;
    private ExecutionMarker? _executionOwner;
    private readonly Dictionary<MouseButton, ExecutionMarker>
        _heldExecutions = new();

    internal MultiMacroHotkeyCoordinator(
        ApplicationSettings settings,
        Func<WindowsGlobalHotkeyService> hotkeyServiceFactory,
        MacroRunner macroRunner,
        Action? terminalPhase1CompletedForTesting = null,
        Func<WindowsGlobalMouseButtonService>? mouseButtonServiceFactory = null,
        Action? executionOwnerReleasedForTesting = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeyServiceFactory);
        ArgumentNullException.ThrowIfNull(macroRunner);

        _macros = settings.Macros.ToArray();
        _hotkeyServiceFactory = hotkeyServiceFactory;
        _mouseButtonServiceFactory = mouseButtonServiceFactory ??
            (() => new WindowsGlobalMouseButtonService());
        _macroRunner = macroRunner;
        _terminalPhase1CompletedForTesting =
            terminalPhase1CompletedForTesting;
        _executionOwnerReleasedForTesting =
            executionOwnerReleasedForTesting;
        _macroIndexes = new Dictionary<MacroId, int>(_macros.Length);
        _bindingsByHotkey = new Dictionary<KeyboardHotkey, RuntimeMacroBinding>();
        _bindingsByMouseButton =
            new Dictionary<MouseButton, RuntimeMacroBinding>();

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
                macro.Trigger,
                macro.ExecutionMode,
                macro.Sequence);

            if (binding.Trigger is KeyboardMacroTrigger keyboard)
            {
                if (!_bindingsByHotkey.TryAdd(keyboard.Hotkey, binding))
                {
                    throw new InvalidOperationException(
                        $"Runtime settings contain duplicate enabled trigger " +
                        $"'{keyboard.Hotkey}'.");
                }
            }
            else if (binding.Trigger is MouseButtonMacroTrigger mouse)
            {
                if (!_bindingsByMouseButton.TryAdd(mouse.Button, binding))
                {
                    throw new InvalidOperationException(
                        $"Runtime settings contain duplicate enabled trigger " +
                        $"'{mouse.Button}'.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Runtime trigger type '{binding.Trigger.GetType().Name}' " +
                    "is not supported.");
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

    internal async Task WaitForIdleForTestingAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate)
            {
                pending = _executions
                    .Select(execution => execution.Completion.Task)
                    .ToArray();
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
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
                snapshot = _bindingsByMouseButton.Count == 0
                    ? ActivateEnabledBindings()
                    : ActivateMixedBindings();
            }

            _activationCompleted.TrySetResult();
            PublishStatusChanged(snapshot);
            return snapshot;
        }
    }

    public void Dispose()
    {
        GlobalHotkeyRegistration[] registrations;
        GlobalMouseButtonRegistration[] mouseRegistrations;
        WindowsGlobalHotkeyService? hotkeyService;
        WindowsGlobalMouseButtonService? mouseButtonService;
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
                UnsubscribeFromMouseButtonService();
                registrations = _registrations.ToArray();
                _registrations.Clear();
                mouseRegistrations = _mouseRegistrations.ToArray();
                _mouseRegistrations.Clear();
                hotkeyService = _hotkeyService;
                _hotkeyService = null;
                mouseButtonService = _mouseButtonService;
                _mouseButtonService = null;
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
            DisposeRegistrations(mouseRegistrations, failures);

            if (hotkeyService is not null)
            {
                TryDispose(hotkeyService, failures);
            }

            if (mouseButtonService is not null)
            {
                TryDispose(mouseButtonService, failures);
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
                        hotkeyService.Register(
                            ((KeyboardMacroTrigger)binding.Trigger).Hotkey);

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

    private MultiMacroRuntimeStatus ActivateMixedBindings()
    {
        RuntimeMacroStatus[] statuses = CreatePendingEnabledStatuses();
        ActivateKeyboardBackend(statuses);
        ActivateMouseBackend(statuses);

        MultiMacroRuntimeState aggregate = DetermineAggregateState(statuses);
        lock (_gate)
        {
            _state = CoordinatorState.Active;
            _executionAllowed = true;
            return SetInitialStatus(aggregate, statuses);
        }
    }

    private void ActivateKeyboardBackend(RuntimeMacroStatus[] statuses)
    {
        if (_bindingsByHotkey.Count == 0)
        {
            return;
        }

        try
        {
            WindowsGlobalHotkeyService service = _hotkeyServiceFactory() ??
                throw new InvalidOperationException(
                    "The global hotkey service factory returned null.");
            lock (_gate)
            {
                _hotkeyService = service;
                SubscribeToHotkeyService(service);
            }

            _ = ObserveBackendCompletionAsync(
                service.MessageLoopCompletion,
                isMouseBackend: false);

            foreach ((KeyboardHotkey hotkey, RuntimeMacroBinding binding) in
                _bindingsByHotkey)
            {
                try
                {
                    GlobalHotkeyRegistration registration = service.Register(hotkey);
                    lock (_gate)
                    {
                        _registrations.Add(registration);
                    }
                    statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                        binding.Id, RuntimeMacroState.Active);
                }
                catch (GlobalHotkeyRegistrationException exception)
                {
                    statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                        binding.Id,
                        RuntimeMacroState.RegistrationFailed,
                        exception.Message);
                }
            }
        }
        catch (Exception exception)
        {
            foreach (RuntimeMacroBinding binding in _bindingsByHotkey.Values)
            {
                statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                    binding.Id,
                    RuntimeMacroState.RegistrationFailed,
                    exception.Message);
            }
        }
    }

    private void ActivateMouseBackend(RuntimeMacroStatus[] statuses)
    {
        try
        {
            WindowsGlobalMouseButtonService service =
                _mouseButtonServiceFactory() ??
                throw new InvalidOperationException(
                    "The global mouse-button service factory returned null.");
            lock (_gate)
            {
                _mouseButtonService = service;
                SubscribeToMouseButtonService(service);
            }

            _ = ObserveBackendCompletionAsync(
                service.MessageLoopCompletion,
                isMouseBackend: true);

            foreach ((MouseButton button, RuntimeMacroBinding binding) in
                _bindingsByMouseButton)
            {
                try
                {
                    GlobalMouseButtonRegistration registration =
                        service.Register(button);
                    lock (_gate)
                    {
                        _mouseRegistrations.Add(registration);
                    }
                    statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                        binding.Id, RuntimeMacroState.Active);
                }
                catch (GlobalMouseButtonRegistrationException exception)
                {
                    statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                        binding.Id,
                        RuntimeMacroState.RegistrationFailed,
                        exception.Message);
                }
            }
        }
        catch (Exception exception)
        {
            foreach (RuntimeMacroBinding binding in _bindingsByMouseButton.Values)
            {
                statuses[_macroIndexes[binding.Id]] = new RuntimeMacroStatus(
                    binding.Id,
                    RuntimeMacroState.RegistrationFailed,
                    exception.Message);
            }
        }
    }

    private async Task ObserveBackendCompletionAsync(
        Task<Exception?> completion,
        bool isMouseBackend)
    {
        await Task.Yield();
        Exception? failure;
        try
        {
            failure = await completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is null)
        {
            return;
        }

        await _activationCompleted.Task.ConfigureAwait(false);

        MultiMacroRuntimeStatus snapshot;
        ExecutionMarker[] executionsToCancel;
        lock (_gate)
        {
            if (_state != CoordinatorState.Active)
            {
                return;
            }

            IEnumerable<RuntimeMacroBinding> affected = isMouseBackend
                ? _bindingsByMouseButton.Values
                : _bindingsByHotkey.Values;
            HashSet<MacroId> affectedIds = affected
                .Select(binding => binding.Id)
                .ToHashSet();
            RuntimeMacroStatus[] statuses = _currentStatus!.Macros
                .Select(status => affectedIds.Contains(status.Id) &&
                        status.State == RuntimeMacroState.Active
                    ? new RuntimeMacroStatus(
                        status.Id,
                        RuntimeMacroState.RuntimeUnavailable,
                        failure.Message)
                    : status)
                .ToArray();

            if (isMouseBackend)
            {
                UnsubscribeFromMouseButtonService();
                _mouseRegistrations.Clear();
                _mouseButtonService = null;
            }
            else
            {
                UnsubscribeFromHotkeyService();
                _registrations.Clear();
                _hotkeyService = null;
            }

            executionsToCancel = _executions
                .Where(item => isMouseBackend
                    ? item.Binding.Trigger is MouseButtonMacroTrigger
                    : item.Binding.Trigger is KeyboardMacroTrigger)
                .ToArray();

            MultiMacroRuntimeState aggregate = DetermineAggregateState(statuses);
            if (!statuses.Any(status => status.State == RuntimeMacroState.Active))
            {
                aggregate = MultiMacroRuntimeState.ServiceUnavailable;
                _executionAllowed = false;
                _state = CoordinatorState.ServiceUnavailable;
            }

            snapshot = new MultiMacroRuntimeStatus(aggregate, statuses);
            _currentStatus = snapshot;
        }

        foreach (ExecutionMarker execution in executionsToCancel)
        {
            execution.Cancel();
        }

        PublishStatusChanged(snapshot);
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
        if (!_bindingsByHotkey.TryGetValue(
                hotkey,
                out RuntimeMacroBinding? binding))
        {
            return;
        }

        AdmitAndSchedule(binding, heldButton: null);
    }

    private void OnMouseButtonDown(MouseButton button)
    {
        if (!_bindingsByMouseButton.TryGetValue(
                button,
                out RuntimeMacroBinding? binding))
        {
            return;
        }

        MouseButton? heldButton =
            binding.ExecutionMode == MacroExecutionMode.RepeatWhileHeld
                ? button
                : null;
        AdmitAndSchedule(binding, heldButton);
    }

    private void OnMouseButtonUp(MouseButton button)
    {
        lock (_gate)
        {
            if (_heldExecutions.TryGetValue(
                    button,
                    out ExecutionMarker? execution))
            {
                execution.IsHeld = false;
            }
        }
    }

    private void AdmitAndSchedule(
        RuntimeMacroBinding binding,
        MouseButton? heldButton)
    {
        ExecutionMarker execution;

        lock (_gate)
        {
            if (!_executionAllowed ||
                _state != CoordinatorState.Active ||
                _executionOwner is not null)
            {
                return;
            }

            execution = new ExecutionMarker(
                binding,
                _lifetimeCancellation.Token,
                heldButton);
            // Admission is the product-level busy/drop linearization point.
            // Every accepted execution owns the one global slot before its
            // asynchronous worker can be scheduled.
            _executionOwner = execution;
            _executions.Add(execution);
            if (heldButton is { } button)
            {
                _heldExecutions.Add(button, execution);
            }
        }

        try
        {
            QueueExecutionWork(() => RunAndCompleteAsync(execution));
        }
        catch (Exception exception)
        {
            OnMacroTaskCompleted(
                execution,
                Task.FromResult<Exception?>(exception));
        }
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
                .RunAsync(execution.Binding.Sequence, execution.CancellationToken)
                .ConfigureAwait(false);
            if (result == MacroRunResult.AlreadyRunning ||
                execution.Binding.ExecutionMode == MacroExecutionMode.Once)
            {
                return null;
            }

            while (true)
            {
                lock (_gate)
                {
                    if (!_executionAllowed ||
                        _state != CoordinatorState.Active ||
                        !execution.IsHeld)
                    {
                        return null;
                    }
                }

                result = await _macroRunner
                    .RunAsync(
                        execution.Binding.Sequence,
                        execution.CancellationToken)
                    .ConfigureAwait(false);
                if (result == MacroRunResult.AlreadyRunning)
                {
                    return null;
                }
            }
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
        finally
        {
            bool ownerReleased = false;
            lock (_gate)
            {
                if (ReferenceEquals(_executionOwner, execution))
                {
                    _executionOwner = null;
                    ownerReleased = true;
                }
                if (execution.HeldButton is { } button &&
                    _heldExecutions.TryGetValue(
                        button,
                        out ExecutionMarker? held) &&
                    ReferenceEquals(held, execution))
                {
                    _heldExecutions.Remove(button);
                }
            }

            if (ownerReleased)
            {
                _executionOwnerReleasedForTesting?.Invoke();
            }
        }
    }

    private async Task RunAndCompleteAsync(ExecutionMarker execution)
    {
        Task<Exception?> executionTask;
        try
        {
            executionTask = RunMacroAsync(execution);
            await executionTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            executionTask = Task.FromException<Exception?>(exception);
        }

        OnMacroTaskCompleted(execution, executionTask);
    }

    private static void QueueExecutionWork(Func<Task> work)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        if (context is not null)
        {
            context.Post(
                static state => _ = ((Func<Task>)state!).Invoke(),
                work);
            return;
        }

        _ = Task.Run(work);
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
            if (ReferenceEquals(_executionOwner, execution))
            {
                _executionOwner = null;
            }
            if (execution.HeldButton is { } button &&
                _heldExecutions.TryGetValue(button, out ExecutionMarker? held) &&
                ReferenceEquals(held, execution))
            {
                _heldExecutions.Remove(button);
            }
        }

        execution.Completion.TrySetResult(failure);
        execution.Dispose();

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

    private void SubscribeToMouseButtonService(
        WindowsGlobalMouseButtonService service)
    {
        service.ButtonDown += OnMouseButtonDown;
        service.ButtonUp += OnMouseButtonUp;
        _mouseSubscribed = true;
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

    private void UnsubscribeFromMouseButtonService()
    {
        if (!_mouseSubscribed || _mouseButtonService is null)
        {
            return;
        }

        _mouseButtonService.ButtonDown -= OnMouseButtonDown;
        _mouseButtonService.ButtonUp -= OnMouseButtonUp;
        _mouseSubscribed = false;
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

    private static void DisposeRegistrations<T>(
        IEnumerable<T> registrations,
        ICollection<Exception> failures)
        where T : IDisposable
    {
        foreach (T registration in registrations)
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

    private sealed class ExecutionMarker : IDisposable
    {
        internal ExecutionMarker(
            RuntimeMacroBinding binding,
            CancellationToken cancellationToken,
            MouseButton? heldButton)
        {
            Binding = binding;
            HeldButton = heldButton;
            IsHeld = heldButton is not null;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        }

        internal RuntimeMacroBinding Binding { get; }

        internal CancellationTokenSource Cancellation { get; }

        internal CancellationToken CancellationToken => Cancellation.Token;

        internal MouseButton? HeldButton { get; }

        internal bool IsHeld { get; set; }

        internal TaskCompletionSource<Exception?> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal ExecutionState State { get; set; } =
            ExecutionState.PendingStart;

        internal void Cancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (Exception)
            {
                // Cancellation has still been requested; terminal cleanup
                // must not wait for or execute callback policy.
            }
        }

        public void Dispose() => Cancellation.Dispose();
    }
}
