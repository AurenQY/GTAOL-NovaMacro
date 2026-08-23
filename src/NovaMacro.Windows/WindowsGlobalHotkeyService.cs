using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NovaMacro.Windows;

public sealed class WindowsGlobalHotkeyService : IDisposable
{
    private const int MaximumRegistrationId = 0xBFFF;

    private const uint WmQuit = 0x0012;
    private const uint WmHotkey = 0x0312;
    private const uint CommandMessage = 0x8001;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyAlt = 0x12;
    private const int KeyDownBit = 0x8000;

    private static readonly TimeSpan ReleasePollInterval =
        TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan ReleaseTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout =
        TimeSpan.FromSeconds(5);

    private readonly object _admissionGate = new();
    private readonly object _callbackGate = new();
    private readonly ConcurrentQueue<MessageCommand> _commands = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource<bool> _messageLoopReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _messageLoopStopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IGlobalHotkeyNativeApi _nativeApi;
    private readonly IHotkeyTriggerWorkScheduler _triggerWorkScheduler;
    private readonly Thread _messageThread;

    private Registration? _currentRegistration;
    private TaskCompletionSource<bool>? _disposeCompletion;
    private Exception? _messageLoopFailure;
    private ServiceState _state = ServiceState.Active;
    private uint _messageThreadId;
    private int _nextRegistrationId = 1;
    private bool _callbacksAllowed = true;

    public WindowsGlobalHotkeyService()
        : this(
            CreateNativeApi(),
            new ThreadPoolHotkeyTriggerWorkScheduler())
    {
    }

    internal WindowsGlobalHotkeyService(IGlobalHotkeyNativeApi nativeApi)
        : this(nativeApi, new ThreadPoolHotkeyTriggerWorkScheduler())
    {
    }

    internal WindowsGlobalHotkeyService(
        IGlobalHotkeyNativeApi nativeApi,
        IHotkeyTriggerWorkScheduler triggerWorkScheduler)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentNullException.ThrowIfNull(triggerWorkScheduler);
        _nativeApi = nativeApi;
        _triggerWorkScheduler = triggerWorkScheduler;

        _messageThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "NovaMacro global hotkey message loop"
        };

        _messageThread.Start();
        _messageLoopReady.Task.GetAwaiter().GetResult();
    }

    private static IGlobalHotkeyNativeApi CreateNativeApi()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Global hotkeys are supported only on Windows.");
        }

        return new Win32GlobalHotkeyNativeApi();
    }

    public event Action<KeyboardHotkey>? Triggered;

    public event Action<KeyboardHotkey, Exception>? TriggerFailed;

    public void Register(KeyboardHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        InvokeLifecycleCommand(() => RegisterCore(hotkey));
    }

    public void Replace(KeyboardHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        InvokeLifecycleCommand(() => ReplaceCore(hotkey));
    }

    public void Unregister()
    {
        InvokeLifecycleCommand(UnregisterCore);
    }

    public void Dispose()
    {
        TaskCompletionSource<bool> completion;
        MessageCommand? cleanupCommand = null;
        bool ownsCleanup = false;

        lock (_admissionGate)
        {
            if (_disposeCompletion is null)
            {
                _disposeCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                ownsCleanup = true;

                if (_state == ServiceState.Active)
                {
                    _state = ServiceState.Disposing;
                    cleanupCommand = EnqueueCommandCore(DisposeRegistrationCore);
                }
                else
                {
                    _state = ServiceState.Disposing;
                }
            }

            completion = _disposeCompletion;
        }

        if (ownsCleanup)
        {
            CompleteDisposal(cleanupCommand, completion);
        }

        completion.Task.GetAwaiter().GetResult();
    }

    private void RegisterCore(KeyboardHotkey hotkey)
    {
        if (_currentRegistration is not null)
        {
            throw new InvalidOperationException(
                "A hotkey is already registered. Use Replace to change it.");
        }

        Registration registration = RegisterNative(
            hotkey,
            AllocateRegistrationId());
        ActivateRegistration(registration);
    }

    private void ReplaceCore(KeyboardHotkey hotkey)
    {
        Registration oldRegistration = _currentRegistration
            ?? throw new InvalidOperationException(
                "No hotkey is registered. Use Register first.");

        if (oldRegistration.Hotkey == hotkey)
        {
            return;
        }

        UnregisterNative(oldRegistration);
        DeactivateRegistration(oldRegistration);

        Registration newRegistration = RegisterNative(
            hotkey,
            AllocateRegistrationId());
        ActivateRegistration(newRegistration);
    }

    private void UnregisterCore()
    {
        Registration? registration = _currentRegistration;
        if (registration is null)
        {
            return;
        }

        UnregisterNative(registration);
        DeactivateRegistration(registration);
    }

    private void DisposeRegistrationCore()
    {
        Registration? registration = DetachRegistration(stopAllCallbacks: true);
        if (registration is null)
        {
            return;
        }

        registration.CancelReleaseWait();
        UnregisterNative(registration);
    }

    private Registration RegisterNative(KeyboardHotkey hotkey, int id)
    {
        uint virtualKey = HotkeyKeyMapper.ToVirtualKey(hotkey.Key);
        uint modifiers = ToNativeModifiers(hotkey.Modifiers);

        if (!_nativeApi.RegisterHotKey(id, modifiers, virtualKey))
        {
            throw CreateWin32Exception(
                $"Windows rejected global hotkey '{hotkey}'. " +
                "The combination may already be in use.");
        }

        return new Registration(id, hotkey);
    }

    private void UnregisterNative(Registration registration)
    {
        if (!_nativeApi.UnregisterHotKey(registration.Id))
        {
            throw CreateWin32Exception(
                $"Failed to unregister global hotkey '{registration.Hotkey}'.");
        }
    }

    private int AllocateRegistrationId()
    {
        if (_nextRegistrationId > MaximumRegistrationId)
        {
            throw new InvalidOperationException(
                "The service has exhausted the Windows application-defined " +
                "global hotkey registration ID range.");
        }

        return _nextRegistrationId++;
    }

    private void ActivateRegistration(Registration registration)
    {
        lock (_callbackGate)
        {
            if (!_callbacksAllowed)
            {
                throw new InvalidOperationException(
                    "The global hotkey service no longer accepts callbacks.");
            }

            Volatile.Write(ref _currentRegistration, registration);
        }
    }

    private void DeactivateRegistration(Registration registration)
    {
        Registration? detached;

        lock (_callbackGate)
        {
            detached = ReferenceEquals(_currentRegistration, registration)
                ? _currentRegistration
                : null;

            if (detached is not null)
            {
                detached.Deactivate();
                Volatile.Write(ref _currentRegistration, null);
            }
        }

        detached?.CancelReleaseWait();
    }

    private Registration? DetachRegistration(bool stopAllCallbacks)
    {
        lock (_callbackGate)
        {
            if (stopAllCallbacks)
            {
                _callbacksAllowed = false;
            }

            Registration? registration = _currentRegistration;
            registration?.Deactivate();
            Volatile.Write(ref _currentRegistration, null);
            return registration;
        }
    }

    private bool TryBeginCallback(Registration registration)
    {
        lock (_callbackGate)
        {
            if (!_callbacksAllowed ||
                !ReferenceEquals(_currentRegistration, registration) ||
                !registration.IsActive)
            {
                return false;
            }

            return true;
        }
    }

    private void RunMessageLoop()
    {
        Exception? loopFailure = null;

        try
        {
            _messageThreadId = _nativeApi.GetCurrentThreadId();
            _nativeApi.EnsureMessageQueue();
            _messageLoopReady.TrySetResult(true);

            while (true)
            {
                int result = _nativeApi.GetMessage(
                    out GlobalHotkeyNativeMessage message);

                if (result == -1)
                {
                    throw CreateWin32Exception(
                        "The global hotkey message loop failed to receive a message.");
                }

                if (result == 0)
                {
                    lock (_admissionGate)
                    {
                        if (_state == ServiceState.Active)
                        {
                            throw new InvalidOperationException(
                                "The global hotkey message loop received an " +
                                "unexpected WM_QUIT message.");
                        }
                    }

                    break;
                }

                if (message.Id == CommandMessage)
                {
                    DrainCommands();
                }
                else if (message.Id == WmHotkey)
                {
                    HandleHotkeyMessage(unchecked((int)message.WParam));
                }
            }
        }
        catch (Exception exception)
        {
            loopFailure = exception;
            HandleTerminalMessageLoopFailure(exception);
            _messageLoopReady.TrySetException(exception);
        }
        finally
        {
            FailPendingCommands(
                loopFailure ?? new ObjectDisposedException(
                    nameof(WindowsGlobalHotkeyService)));
            _messageLoopStopped.TrySetResult(true);
        }
    }

    private void HandleTerminalMessageLoopFailure(Exception exception)
    {
        Registration? registration;

        lock (_admissionGate)
        {
            _messageLoopFailure = exception;

            if (_state == ServiceState.Active)
            {
                _state = ServiceState.Failed;
            }

            lock (_callbackGate)
            {
                _callbacksAllowed = false;
                registration = _currentRegistration;
                registration?.Deactivate();
                Volatile.Write(ref _currentRegistration, null);
            }
        }

        registration?.CancelReleaseWait();
    }

    private void HandleHotkeyMessage(int registrationId)
    {
        Registration? registration = Volatile.Read(ref _currentRegistration);

        if (registration is null ||
            registration.Id != registrationId ||
            !registration.TryBeginReleaseWait())
        {
            return;
        }

        _triggerWorkScheduler.Queue(
            () => ProcessTriggerAsync(registration));
    }

    private async Task ProcessTriggerAsync(Registration registration)
    {
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                registration.ReleaseCancellationToken);

            await WaitForPhysicalReleaseAsync(
                    registration.Hotkey,
                    cancellation.Token)
                .ConfigureAwait(false);

            PublishTriggered(registration);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested || !registration.IsActive)
        {
        }
        catch (Exception exception)
        {
            PublishTriggerFailed(registration, exception);
        }
        finally
        {
            registration.EndReleaseWait();
        }
    }

    private void PublishTriggered(Registration registration)
    {
        InvokeTriggeredSubscribers(registration);
    }

    private void PublishTriggerFailed(
        Registration registration,
        Exception exception)
    {
        InvokeTriggerFailedSubscribers(registration, exception);
    }

    private void InvokeTriggeredSubscribers(Registration registration)
    {
        Action<KeyboardHotkey>? subscribers = Triggered;
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            if (!TryBeginCallback(registration))
            {
                break;
            }

            try
            {
                ((Action<KeyboardHotkey>)subscriber)(registration.Hotkey);
            }
            catch (Exception)
            {
            }
        }
    }

    private void InvokeTriggerFailedSubscribers(
        Registration registration,
        Exception exception)
    {
        Action<KeyboardHotkey, Exception>? subscribers = TriggerFailed;
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            if (!TryBeginCallback(registration))
            {
                break;
            }

            try
            {
                ((Action<KeyboardHotkey, Exception>)subscriber)(
                    registration.Hotkey,
                    exception);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task WaitForPhysicalReleaseAsync(
        KeyboardHotkey hotkey,
        CancellationToken cancellationToken)
    {
        int triggerVirtualKey = checked((int)HotkeyKeyMapper.ToVirtualKey(hotkey.Key));
        long startedAt = Stopwatch.GetTimestamp();

        while (IsAnyConfiguredKeyDown(hotkey, triggerVirtualKey))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Stopwatch.GetElapsedTime(startedAt) >= ReleaseTimeout)
            {
                throw new TimeoutException(
                    $"The physical keys for hotkey '{hotkey}' were not all " +
                    "released within five seconds.");
            }

            await Task.Delay(ReleasePollInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private bool IsAnyConfiguredKeyDown(
        KeyboardHotkey hotkey,
        int triggerVirtualKey)
    {
        if (IsKeyDown(triggerVirtualKey))
        {
            return true;
        }

        HotkeyModifiers modifiers = hotkey.Modifiers;

        return
            ((modifiers & HotkeyModifiers.Control) != 0 &&
                IsKeyDown(VirtualKeyControl)) ||
            ((modifiers & HotkeyModifiers.Alt) != 0 &&
                IsKeyDown(VirtualKeyAlt)) ||
            ((modifiers & HotkeyModifiers.Shift) != 0 &&
                IsKeyDown(VirtualKeyShift));
    }

    private bool IsKeyDown(int virtualKey)
    {
        return (_nativeApi.GetAsyncKeyState(virtualKey) & KeyDownBit) != 0;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint nativeModifiers = ModNoRepeat;

        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            nativeModifiers |= ModControl;
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            nativeModifiers |= ModAlt;
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            nativeModifiers |= ModShift;
        }

        return nativeModifiers;
    }

    private void InvokeLifecycleCommand(Action action)
    {
        MessageCommand command;

        lock (_admissionGate)
        {
            ThrowIfNotActive();
            command = EnqueueCommandCore(action);
        }

        WaitForCommand(command);
    }

    private MessageCommand EnqueueCommandCore(Action action)
    {
        var command = new MessageCommand(action);
        _commands.Enqueue(command);

        if (!_nativeApi.PostThreadMessage(
                Volatile.Read(ref _messageThreadId),
                CommandMessage))
        {
            command.Fail(CreateWin32Exception(
                "Failed to send a command to the global hotkey message loop."));
        }

        return command;
    }

    private void WaitForCommand(MessageCommand command)
    {
        Task completedTask = Task.WhenAny(
                command.Completion,
                _messageLoopStopped.Task)
            .GetAwaiter()
            .GetResult();

        if (completedTask != command.Completion && !command.Completion.IsCompleted)
        {
            command.Fail(CreateMessageLoopStoppedException());
        }

        command.Completion.GetAwaiter().GetResult();
    }

    private void CompleteDisposal(
        MessageCommand? cleanupCommand,
        TaskCompletionSource<bool> completion)
    {
        var failures = new List<Exception>();

        if (cleanupCommand is not null)
        {
            try
            {
                WaitForCommand(cleanupCommand);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        Registration? registration = DetachRegistration(stopAllCallbacks: true);
        registration?.CancelReleaseWait();
        _lifetimeCancellation.Cancel();

        if (!_messageLoopStopped.Task.IsCompleted &&
            !_nativeApi.PostThreadMessage(
                Volatile.Read(ref _messageThreadId),
                WmQuit))
        {
            failures.Add(CreateWin32Exception(
                "Failed to request shutdown of the global hotkey message loop."));
        }

        if (_nativeApi.GetCurrentThreadId() != Volatile.Read(ref _messageThreadId) &&
            !_messageThread.Join(ShutdownTimeout))
        {
            failures.Add(new TimeoutException(
                "The global hotkey message thread did not stop within five seconds."));
        }

        Exception? loopFailure;

        lock (_admissionGate)
        {
            _state = ServiceState.Disposed;
            loopFailure = _messageLoopFailure;
        }

        if (loopFailure is not null &&
            !failures.Any(failure => ReferenceEquals(failure, loopFailure)))
        {
            failures.Add(loopFailure);
        }

        if (failures.Count == 0)
        {
            completion.TrySetResult(true);
        }
        else if (failures.Count == 1)
        {
            completion.TrySetException(failures[0]);
        }
        else
        {
            completion.TrySetException(new AggregateException(
                "One or more global hotkey cleanup operations failed.",
                failures));
        }
    }

    private void ThrowIfNotActive()
    {
        switch (_state)
        {
            case ServiceState.Active:
                return;

            case ServiceState.Failed:
                throw new InvalidOperationException(
                    "The global hotkey message loop has terminated.",
                    _messageLoopFailure);

            default:
                throw new ObjectDisposedException(
                    nameof(WindowsGlobalHotkeyService));
        }
    }

    private Exception CreateMessageLoopStoppedException()
    {
        Exception? failure;

        lock (_admissionGate)
        {
            failure = _messageLoopFailure;
        }

        return failure is null
            ? new ObjectDisposedException(nameof(WindowsGlobalHotkeyService))
            : new InvalidOperationException(
                "The global hotkey message loop stopped unexpectedly.",
                failure);
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out MessageCommand? command))
        {
            command.Execute();
        }
    }

    private void FailPendingCommands(Exception exception)
    {
        while (_commands.TryDequeue(out MessageCommand? command))
        {
            command.Fail(exception);
        }
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    private enum ServiceState
    {
        Active,
        Disposing,
        Disposed,
        Failed
    }

    private sealed class Registration
    {
        private readonly CancellationTokenSource _releaseCancellation = new();
        private int _isActive = 1;
        private int _releaseWaitInProgress;

        internal Registration(int id, KeyboardHotkey hotkey)
        {
            Id = id;
            Hotkey = hotkey;
        }

        internal int Id { get; }

        internal KeyboardHotkey Hotkey { get; }

        internal CancellationToken ReleaseCancellationToken =>
            _releaseCancellation.Token;

        internal bool IsActive => Volatile.Read(ref _isActive) != 0;

        internal bool TryBeginReleaseWait()
        {
            return IsActive &&
                Interlocked.CompareExchange(ref _releaseWaitInProgress, 1, 0) == 0;
        }

        internal void EndReleaseWait()
        {
            Interlocked.Exchange(ref _releaseWaitInProgress, 0);
        }

        internal void Deactivate()
        {
            Volatile.Write(ref _isActive, 0);
        }

        internal void CancelReleaseWait()
        {
            _releaseCancellation.Cancel();
        }
    }

    private sealed class MessageCommand
    {
        private readonly Action _action;
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _claimed;

        internal MessageCommand(Action action)
        {
            _action = action;
        }

        internal Task Completion => _completion.Task;

        internal void Execute()
        {
            if (Interlocked.Exchange(ref _claimed, 1) != 0)
            {
                return;
            }

            try
            {
                _action();
                _completion.SetResult(true);
            }
            catch (Exception exception)
            {
                _completion.SetException(exception);
            }
        }

        internal void Fail(Exception exception)
        {
            if (Interlocked.Exchange(ref _claimed, 1) == 0)
            {
                _completion.SetException(exception);
            }
        }
    }
}
