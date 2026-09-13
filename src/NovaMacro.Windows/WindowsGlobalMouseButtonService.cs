using System.Collections.Concurrent;
using System.ComponentModel;
using NovaMacro.Core;

namespace NovaMacro.Windows;

public sealed class WindowsGlobalMouseButtonService : IDisposable
{
    private const uint WmQuit = 0x0012;
    private const uint CommandMessage = 0x8002;
    private const int HookAction = 0;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly object _admissionGate = new();
    private readonly object _callbackGate = new();
    private readonly ConcurrentQueue<MessageCommand> _commands = new();
    private readonly Queue<ButtonNotification> _notifications = new();
    private readonly Dictionary<MouseButton, Registration> _registrations = new();
    private readonly IGlobalMouseButtonNativeApi _nativeApi;
    private readonly IHotkeyTriggerWorkScheduler _eventScheduler;
    private readonly NativeMethods.LowLevelMouseHookProcedure _hookProcedure;
    private readonly Thread _messageThread;
    private readonly TaskCompletionSource<bool> _messageLoopReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _messageLoopStopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception?> _messageLoopCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ServiceState _state = ServiceState.Active;
    private TaskCompletionSource<bool>? _disposeCompletion;
    private uint _messageThreadId;
    private nint _hookHandle;
    private Exception? _messageLoopFailure;
    private bool _callbacksAllowed = true;
    private bool _notificationPumpScheduled;

    public WindowsGlobalMouseButtonService()
        : this(CreateNativeApi(), new ThreadPoolHotkeyTriggerWorkScheduler())
    {
    }

    internal WindowsGlobalMouseButtonService(
        IGlobalMouseButtonNativeApi nativeApi,
        IHotkeyTriggerWorkScheduler? eventScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _eventScheduler = eventScheduler ??
            new ThreadPoolHotkeyTriggerWorkScheduler();
        _hookProcedure = HookCallback;
        _messageThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "NovaMacro global mouse-button message loop"
        };
        _messageThread.Start();
        _messageLoopReady.Task.GetAwaiter().GetResult();
    }

    private static IGlobalMouseButtonNativeApi CreateNativeApi()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Global mouse-button triggers are supported only on Windows.");
        }

        return new Win32GlobalMouseButtonNativeApi();
    }

    public event Action<MouseButton>? ButtonDown;

    public event Action<MouseButton>? ButtonUp;

    public Task<Exception?> MessageLoopCompletion => _messageLoopCompletion.Task;

    public GlobalMouseButtonRegistration Register(MouseButton button)
    {
        ValidateTriggerButton(button);
        GlobalMouseButtonRegistration? result = null;
        InvokeLifecycleCommand(() => result = RegisterCore(button));
        return result!;
    }

    public void Dispose()
    {
        TaskCompletionSource<bool> completion;
        MessageCommand? cleanup = null;
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
                    lock (_callbackGate)
                    {
                        _callbacksAllowed = false;
                    }
                    cleanup = EnqueueCommandCore(DisposeRegistrationsCore);
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
            CompleteDisposal(cleanup, completion);
        }

        completion.Task.GetAwaiter().GetResult();
    }

    internal void DisposeRegistration(GlobalMouseButtonRegistration handle)
    {
        MessageCommand command;
        lock (_admissionGate)
        {
            if (!handle.IsOwnedBy(this))
            {
                return;
            }

            if (_state != ServiceState.Active)
            {
                handle.Invalidate(this);
                return;
            }

            command = EnqueueCommandCore(() => UnregisterCore(handle));
        }

        WaitForCommand(command);
    }

    private GlobalMouseButtonRegistration RegisterCore(MouseButton button)
    {
        lock (_callbackGate)
        {
            if (_registrations.ContainsKey(button))
            {
                throw new InvalidOperationException(
                    $"Global mouse button '{button}' is already registered.");
            }
        }

        EnsureHookInstalled(button);
        var handle = new GlobalMouseButtonRegistration(this, button);
        var registration = new Registration(handle);

        lock (_callbackGate)
        {
            if (!_callbacksAllowed)
            {
                throw new ObjectDisposedException(
                    nameof(WindowsGlobalMouseButtonService));
            }
            _registrations.Add(button, registration);
        }

        return handle;
    }

    private void EnsureHookInstalled(MouseButton button)
    {
        if (_hookHandle != 0)
        {
            return;
        }

        MouseHookInstallResult result =
            _nativeApi.InstallLowLevelMouseHook(_hookProcedure);
        if (result.HookHandle == 0)
        {
            throw new GlobalMouseButtonRegistrationException(
                button,
                result.ErrorCode,
                $"Windows could not install the global mouse hook for '{button}'. " +
                $"Native error code: {result.ErrorCode}.");
        }

        _hookHandle = result.HookHandle;
    }

    private void UnregisterCore(GlobalMouseButtonRegistration handle)
    {
        Registration? registration;
        bool removeHook;
        lock (_callbackGate)
        {
            registration = _registrations.TryGetValue(
                    handle.Button,
                    out Registration? candidate) &&
                ReferenceEquals(candidate.Handle, handle)
                    ? candidate
                    : null;
            if (registration is not null)
            {
                _registrations.Remove(handle.Button);
                registration.Deactivate();
                handle.Invalidate(this);
            }

            removeHook = _registrations.Count == 0;
        }

        if (registration is null)
        {
            handle.Invalidate(this);
            return;
        }

        if (removeHook)
        {
            RemoveHook();
        }
    }

    private void DisposeRegistrationsCore()
    {
        Registration[] registrations;
        lock (_callbackGate)
        {
            registrations = _registrations.Values.ToArray();
            _registrations.Clear();
            _notifications.Clear();
            foreach (Registration registration in registrations)
            {
                registration.Deactivate();
                registration.Handle.Invalidate(this);
            }
        }

        RemoveHook();
    }

    private void RemoveHook()
    {
        nint hookHandle = _hookHandle;
        if (hookHandle == 0)
        {
            return;
        }

        _hookHandle = 0;
        MouseHookRemovalResult result =
            _nativeApi.RemoveLowLevelMouseHook(hookHandle);
        if (!result.Succeeded)
        {
            throw new Win32Exception(
                result.ErrorCode,
                "Windows could not remove the global mouse hook.");
        }
    }

    private nint HookCallback(int code, nuint message, nint data)
    {
        nint hookHandle = Volatile.Read(ref _hookHandle);
        if (code != HookAction)
        {
            return _nativeApi.CallNextMouseHook(
                hookHandle, code, message, data);
        }

        MouseButton button;
        bool isDown;
        try
        {
            if (!TryDecodeButtonMessage(
                    message, data, out button, out isDown))
            {
                return _nativeApi.CallNextMouseHook(
                    hookHandle, code, message, data);
            }
        }
        catch (Exception)
        {
            return _nativeApi.CallNextMouseHook(
                hookHandle, code, message, data);
        }

        Registration? registration;
        ButtonNotification? notification = null;
        lock (_callbackGate)
        {
            if (!_callbacksAllowed ||
                !_registrations.TryGetValue(button, out registration) ||
                !registration.IsActive)
            {
                registration = null;
            }
            else if (isDown)
            {
                if (registration.TryPress())
                {
                    notification = new ButtonNotification(registration, true);
                }
            }
            else if (registration.TryRelease())
            {
                notification = new ButtonNotification(registration, false);
            }
        }

        if (registration is null)
        {
            return _nativeApi.CallNextMouseHook(
                hookHandle, code, message, data);
        }

        if (notification is not null)
        {
            QueueNotification(notification);
        }

        return 1;
    }

    private bool TryDecodeButtonMessage(
        nuint message,
        nint data,
        out MouseButton button,
        out bool isDown)
    {
        isDown = message == NativeMethods.WmXButtonDown;
        if (!isDown && message != NativeMethods.WmXButtonUp)
        {
            button = default;
            return false;
        }

        ushort xButton = (ushort)(_nativeApi.ReadMouseData(data) >> 16);
        button = xButton switch
        {
            NativeMethods.XButton1 => MouseButton.XButton1,
            NativeMethods.XButton2 => MouseButton.XButton2,
            _ => default
        };
        return xButton is NativeMethods.XButton1 or NativeMethods.XButton2;
    }

    private void QueueNotification(ButtonNotification notification)
    {
        bool startPump = false;
        lock (_callbackGate)
        {
            if (!_callbacksAllowed || !notification.Registration.IsActive)
            {
                return;
            }

            _notifications.Enqueue(notification);
            if (!_notificationPumpScheduled)
            {
                _notificationPumpScheduled = true;
                startPump = true;
            }
        }

        if (startPump)
        {
            try
            {
                _eventScheduler.Queue(ProcessNotificationsAsync);
            }
            catch (Exception)
            {
                lock (_callbackGate)
                {
                    _notificationPumpScheduled = false;
                    _notifications.Clear();
                }
            }
        }
    }

    private Task ProcessNotificationsAsync()
    {
        while (true)
        {
            ButtonNotification notification;
            lock (_callbackGate)
            {
                if (!_callbacksAllowed)
                {
                    _notifications.Clear();
                    _notificationPumpScheduled = false;
                    return Task.CompletedTask;
                }

                if (_notifications.Count == 0)
                {
                    _notificationPumpScheduled = false;
                    return Task.CompletedTask;
                }

                notification = _notifications.Dequeue();
                if (!notification.Registration.IsActive ||
                    !_registrations.TryGetValue(
                        notification.Registration.Button,
                        out Registration? current) ||
                    !ReferenceEquals(current, notification.Registration))
                {
                    continue;
                }
            }

            Delegate[] subscribers = (notification.IsDown
                    ? ButtonDown
                    : ButtonUp)?
                .GetInvocationList() ?? Array.Empty<Delegate>();

            foreach (Delegate subscriber in subscribers)
            {
                lock (_callbackGate)
                {
                    if (!_callbacksAllowed ||
                        !notification.Registration.IsActive)
                    {
                        break;
                    }
                }

                try
                {
                    ((Action<MouseButton>)subscriber)(
                        notification.Registration.Button);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private void RunMessageLoop()
    {
        Exception? failure = null;
        try
        {
            _messageThreadId = _nativeApi.GetCurrentThreadId();
            _nativeApi.EnsureMessageQueue();
            _messageLoopReady.TrySetResult(true);

            while (true)
            {
                int result = _nativeApi.GetMessage(
                    out GlobalMouseNativeMessage message);
                if (result == -1)
                {
                    throw new Win32Exception(
                        "The global mouse message loop failed.");
                }

                if (result == 0)
                {
                    lock (_admissionGate)
                    {
                        if (_state == ServiceState.Active)
                        {
                            throw new InvalidOperationException(
                                "The global mouse message loop received an " +
                                "unexpected WM_QUIT message.");
                        }
                    }
                    break;
                }

                if (message.Id == CommandMessage)
                {
                    DrainCommands();
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            HandleTerminalFailure(exception);
            _messageLoopReady.TrySetException(exception);
        }
        finally
        {
            try
            {
                DisposeRegistrationsCore();
            }
            catch (Exception cleanupFailure)
            {
                failure = failure is null
                    ? cleanupFailure
                    : new AggregateException(failure, cleanupFailure);
            }

            FailPendingCommands(
                failure ?? new ObjectDisposedException(
                    nameof(WindowsGlobalMouseButtonService)));
            _messageLoopFailure = failure;
            _messageLoopCompletion.TrySetResult(failure);
            _messageLoopStopped.TrySetResult(true);
        }
    }

    private void HandleTerminalFailure(Exception exception)
    {
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
            }
        }
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
                Volatile.Read(ref _messageThreadId), CommandMessage))
        {
            command.Fail(new Win32Exception(
                "Failed to send a command to the global mouse message loop."));
        }
        return command;
    }

    private void WaitForCommand(MessageCommand command)
    {
        Task completed = Task.WhenAny(
                command.Completion,
                _messageLoopStopped.Task)
            .GetAwaiter().GetResult();
        if (completed != command.Completion && !command.Completion.IsCompleted)
        {
            command.Fail(CreateMessageLoopStoppedException());
        }
        command.Completion.GetAwaiter().GetResult();
    }

    private void CompleteDisposal(
        MessageCommand? cleanup,
        TaskCompletionSource<bool> completion)
    {
        var failures = new List<Exception>();
        if (cleanup is not null)
        {
            try
            {
                WaitForCommand(cleanup);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (!_messageLoopStopped.Task.IsCompleted &&
            !_nativeApi.PostThreadMessage(
                Volatile.Read(ref _messageThreadId), WmQuit))
        {
            failures.Add(new Win32Exception(
                "Failed to stop the global mouse message loop."));
        }

        if (_nativeApi.GetCurrentThreadId() !=
                Volatile.Read(ref _messageThreadId) &&
            !_messageThread.Join(ShutdownTimeout))
        {
            failures.Add(new TimeoutException(
                "The global mouse message thread did not stop within five seconds."));
        }

        lock (_admissionGate)
        {
            _state = ServiceState.Disposed;
            if (_messageLoopFailure is { } loopFailure &&
                !failures.Any(item => ReferenceEquals(item, loopFailure)))
            {
                failures.Add(loopFailure);
            }
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
                "Global mouse-button service cleanup failed.", failures));
        }
    }

    private void ThrowIfNotActive()
    {
        if (_state == ServiceState.Active)
        {
            return;
        }
        if (_state == ServiceState.Failed)
        {
            throw new InvalidOperationException(
                "The global mouse message loop has terminated.",
                _messageLoopFailure);
        }
        throw new ObjectDisposedException(
            nameof(WindowsGlobalMouseButtonService));
    }

    private Exception CreateMessageLoopStoppedException()
    {
        return _messageLoopFailure is null
            ? new ObjectDisposedException(
                nameof(WindowsGlobalMouseButtonService))
            : new InvalidOperationException(
                "The global mouse message loop stopped unexpectedly.",
                _messageLoopFailure);
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

    private static void ValidateTriggerButton(MouseButton button)
    {
        if (button is not MouseButton.XButton1 and not MouseButton.XButton2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                "Only XButton1 and XButton2 can be global mouse triggers.");
        }
    }

    private enum ServiceState { Active, Disposing, Disposed, Failed }

    private sealed class Registration
    {
        private int _active = 1;
        private int _held;

        internal Registration(GlobalMouseButtonRegistration handle)
        {
            Handle = handle;
        }

        internal GlobalMouseButtonRegistration Handle { get; }
        internal MouseButton Button => Handle.Button;
        internal bool IsActive => Volatile.Read(ref _active) != 0;
        internal bool TryPress() =>
            IsActive && Interlocked.CompareExchange(ref _held, 1, 0) == 0;
        internal bool TryRelease() =>
            Interlocked.CompareExchange(ref _held, 0, 1) == 1;
        internal void Deactivate()
        {
            Volatile.Write(ref _active, 0);
            Volatile.Write(ref _held, 0);
        }
    }

    private sealed record ButtonNotification(
        Registration Registration,
        bool IsDown);

    private sealed class MessageCommand
    {
        private readonly Action _action;
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _claimed;

        internal MessageCommand(Action action) => _action = action;
        internal Task Completion => _completion.Task;
        internal void Execute()
        {
            if (Interlocked.Exchange(ref _claimed, 1) != 0) return;
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
