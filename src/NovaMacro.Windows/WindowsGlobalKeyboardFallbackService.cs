using System.Collections.Concurrent;
using System.ComponentModel;

namespace NovaMacro.Windows;

public sealed class WindowsGlobalKeyboardFallbackService : IDisposable
{
    private const uint WmQuit = 0x0012;
    private const uint CommandMessage = 0x8003;
    private const int HookAction = 0;

    private const uint LowLevelInjected = 0x00000010;
    private const uint LowLevelLowerIntegrityInjected = 0x00000002;
    private const uint LowLevelAltDown = 0x00000020;

    private const uint VirtualKeyShift = 0x10;
    private const uint VirtualKeyControl = 0x11;
    private const uint VirtualKeyAlt = 0x12;
    private const uint VirtualKeyLeftWindows = 0x5B;
    private const uint VirtualKeyRightWindows = 0x5C;
    private const uint VirtualKeyLeftShift = 0xA0;
    private const uint VirtualKeyRightShift = 0xA1;
    private const uint VirtualKeyLeftControl = 0xA2;
    private const uint VirtualKeyRightControl = 0xA3;
    private const uint VirtualKeyLeftAlt = 0xA4;
    private const uint VirtualKeyRightAlt = 0xA5;

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly object _admissionGate = new();
    private readonly object _callbackGate = new();
    private readonly ConcurrentQueue<MessageCommand> _commands = new();
    private readonly Queue<TriggerNotification> _notifications = new();
    private readonly Dictionary<KeyboardHotkey, Registration> _registrations =
        new();
    private readonly HashSet<uint> _pressedVirtualKeys = new();
    private readonly IGlobalKeyboardFallbackNativeApi _nativeApi;
    private readonly IHotkeyTriggerWorkScheduler _eventScheduler;
    private readonly NativeMethods.LowLevelKeyboardHookProcedure _hookProcedure;
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

    public WindowsGlobalKeyboardFallbackService()
        : this(
            CreateNativeApi(),
            new ThreadPoolHotkeyTriggerWorkScheduler())
    {
    }

    internal WindowsGlobalKeyboardFallbackService(
        IGlobalKeyboardFallbackNativeApi nativeApi,
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
            Name = "NovaMacro keyboard fallback message loop"
        };
        _messageThread.Start();
        _messageLoopReady.Task.GetAwaiter().GetResult();
    }

    private static IGlobalKeyboardFallbackNativeApi CreateNativeApi()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Keyboard hotkey fallback is supported only on Windows.");
        }

        return new Win32GlobalKeyboardFallbackNativeApi();
    }

    public event Action<KeyboardHotkey>? Triggered;

    public Task<Exception?> MessageLoopCompletion => _messageLoopCompletion.Task;

    public GlobalKeyboardFallbackRegistration Register(KeyboardHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        GlobalKeyboardFallbackRegistration? result = null;
        InvokeLifecycleCommand(() => result = RegisterCore(hotkey));
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

    internal void DisposeRegistration(GlobalKeyboardFallbackRegistration handle)
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

    private GlobalKeyboardFallbackRegistration RegisterCore(
        KeyboardHotkey hotkey)
    {
        lock (_callbackGate)
        {
            if (_registrations.ContainsKey(hotkey))
            {
                throw new InvalidOperationException(
                    $"Keyboard fallback hotkey '{hotkey}' is already registered.");
            }
        }

        EnsureHookInstalled(hotkey);
        var handle = new GlobalKeyboardFallbackRegistration(this, hotkey);
        var registration = new Registration(
            handle,
            HotkeyKeyMapper.ToVirtualKey(hotkey.Key));

        lock (_callbackGate)
        {
            if (!_callbacksAllowed)
            {
                throw new ObjectDisposedException(
                    nameof(WindowsGlobalKeyboardFallbackService));
            }

            _registrations.Add(hotkey, registration);
        }

        return handle;
    }

    private void EnsureHookInstalled(KeyboardHotkey hotkey)
    {
        if (_hookHandle != 0)
        {
            return;
        }

        KeyboardHookInstallResult result =
            _nativeApi.InstallLowLevelKeyboardHook(_hookProcedure);
        if (result.HookHandle == 0)
        {
            throw new GlobalKeyboardFallbackRegistrationException(
                hotkey,
                result.ErrorCode,
                $"Windows could not install the keyboard fallback hook for " +
                $"'{hotkey}'. Native error code: {result.ErrorCode}.");
        }

        _hookHandle = result.HookHandle;
    }

    private void UnregisterCore(GlobalKeyboardFallbackRegistration handle)
    {
        Registration? registration;
        bool removeHook;

        lock (_callbackGate)
        {
            registration = _registrations.TryGetValue(
                    handle.Hotkey,
                    out Registration? candidate) &&
                ReferenceEquals(candidate.Handle, handle)
                    ? candidate
                    : null;
            if (registration is not null)
            {
                _registrations.Remove(handle.Hotkey);
                registration.Deactivate();
                handle.Invalidate(this);
                RemoveQueuedNotifications(registration);
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
            _pressedVirtualKeys.Clear();
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
        lock (_callbackGate)
        {
            _pressedVirtualKeys.Clear();
        }

        KeyboardHookRemovalResult result =
            _nativeApi.RemoveLowLevelKeyboardHook(hookHandle);
        if (!result.Succeeded)
        {
            throw new Win32Exception(
                result.ErrorCode,
                "Windows could not remove the keyboard fallback hook.");
        }
    }

    private nint HookCallback(int code, nuint message, nint data)
    {
        nint hookHandle = Volatile.Read(ref _hookHandle);

        try
        {
            if (code == HookAction && TryGetTransition(message, out bool isDown))
            {
                KeyboardHookEvent keyboardEvent =
                    _nativeApi.ReadKeyboardEvent(data);
                if (!IsInjected(keyboardEvent.Flags))
                {
                    ProcessPhysicalTransition(
                        keyboardEvent,
                        isDown);
                }
            }
        }
        catch (Exception)
        {
            // Native hooks must never allow managed failures to escape.
        }

        try
        {
            // This backend observes; it never takes input away from another app.
            return _nativeApi.CallNextKeyboardHook(
                hookHandle,
                code,
                message,
                data);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void ProcessPhysicalTransition(
        KeyboardHookEvent keyboardEvent,
        bool isDown)
    {
        TriggerNotification? notification = null;

        lock (_callbackGate)
        {
            if (!_callbacksAllowed)
            {
                return;
            }

            uint virtualKey = keyboardEvent.VirtualKey;
            if (isDown)
            {
                if (!_pressedVirtualKeys.Add(virtualKey))
                {
                    return;
                }

                if (!IsModifierKey(virtualKey))
                {
                    HotkeyModifiers modifiers = GetPressedModifiers(
                        keyboardEvent.Flags);
                    if (!IsWindowsModifierDown() &&
                        TryFindRegistration(
                            virtualKey,
                            modifiers,
                            out Registration registration))
                    {
                        notification = new TriggerNotification(registration);
                    }
                }
            }
            else
            {
                _pressedVirtualKeys.Remove(virtualKey);
            }
        }

        if (notification is not null)
        {
            QueueNotification(notification);
        }
    }

    private bool TryFindRegistration(
        uint virtualKey,
        HotkeyModifiers modifiers,
        out Registration registration)
    {
        Registration? match = _registrations.Values.FirstOrDefault(candidate =>
            candidate.IsActive &&
            candidate.VirtualKey == virtualKey &&
            candidate.Hotkey.Modifiers == modifiers);
        registration = match!;
        return match is not null;
    }

    private HotkeyModifiers GetPressedModifiers(uint eventFlags)
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if (IsAnyPressed(
                VirtualKeyControl,
                VirtualKeyLeftControl,
                VirtualKeyRightControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }
        if ((eventFlags & LowLevelAltDown) != 0 ||
            IsAnyPressed(VirtualKeyAlt, VirtualKeyLeftAlt, VirtualKeyRightAlt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }
        if (IsAnyPressed(
                VirtualKeyShift,
                VirtualKeyLeftShift,
                VirtualKeyRightShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }
        return modifiers;
    }

    private bool IsWindowsModifierDown()
    {
        return IsAnyPressed(VirtualKeyLeftWindows, VirtualKeyRightWindows);
    }

    private bool IsAnyPressed(uint first, uint second)
    {
        return _pressedVirtualKeys.Contains(first) ||
            _pressedVirtualKeys.Contains(second);
    }

    private bool IsAnyPressed(uint first, uint second, uint third)
    {
        return _pressedVirtualKeys.Contains(first) ||
            _pressedVirtualKeys.Contains(second) ||
            _pressedVirtualKeys.Contains(third);
    }

    private static bool IsModifierKey(uint virtualKey)
    {
        return virtualKey is
            VirtualKeyShift or VirtualKeyControl or VirtualKeyAlt or
            VirtualKeyLeftWindows or VirtualKeyRightWindows or
            VirtualKeyLeftShift or VirtualKeyRightShift or
            VirtualKeyLeftControl or VirtualKeyRightControl or
            VirtualKeyLeftAlt or VirtualKeyRightAlt;
    }

    private static bool TryGetTransition(nuint message, out bool isDown)
    {
        isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        return isDown ||
            message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
    }

    private static bool IsInjected(uint flags)
    {
        return (flags & (LowLevelInjected | LowLevelLowerIntegrityInjected)) != 0;
    }

    private void QueueNotification(TriggerNotification notification)
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
            TriggerNotification notification;
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
                        notification.Registration.Hotkey,
                        out Registration? current) ||
                    !ReferenceEquals(current, notification.Registration))
                {
                    continue;
                }
            }

            Delegate[] subscribers = Triggered?.GetInvocationList() ??
                Array.Empty<Delegate>();
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
                    ((Action<KeyboardHotkey>)subscriber)(
                        notification.Registration.Hotkey);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private void RemoveQueuedNotifications(Registration registration)
    {
        if (_notifications.Count == 0)
        {
            return;
        }

        TriggerNotification[] retained = _notifications
            .Where(item => !ReferenceEquals(item.Registration, registration))
            .ToArray();
        _notifications.Clear();
        foreach (TriggerNotification notification in retained)
        {
            _notifications.Enqueue(notification);
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
                    out GlobalKeyboardFallbackNativeMessage message);
                if (result == -1)
                {
                    throw new Win32Exception(
                        "The keyboard fallback message loop failed.");
                }

                if (result == 0)
                {
                    lock (_admissionGate)
                    {
                        if (_state == ServiceState.Active)
                        {
                            throw new InvalidOperationException(
                                "The keyboard fallback message loop received " +
                                "an unexpected WM_QUIT message.");
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
                    nameof(WindowsGlobalKeyboardFallbackService)));
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
                Volatile.Read(ref _messageThreadId),
                CommandMessage))
        {
            command.Fail(new Win32Exception(
                "Failed to send a command to the keyboard fallback message loop."));
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
                Volatile.Read(ref _messageThreadId),
                WmQuit))
        {
            failures.Add(new Win32Exception(
                "Failed to stop the keyboard fallback message loop."));
        }

        if (_nativeApi.GetCurrentThreadId() !=
                Volatile.Read(ref _messageThreadId) &&
            !_messageThread.Join(ShutdownTimeout))
        {
            failures.Add(new TimeoutException(
                "The keyboard fallback message thread did not stop within " +
                "five seconds."));
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
                "Keyboard fallback service cleanup failed.",
                failures));
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
                "The keyboard fallback message loop has terminated.",
                _messageLoopFailure);
        }
        throw new ObjectDisposedException(
            nameof(WindowsGlobalKeyboardFallbackService));
    }

    private Exception CreateMessageLoopStoppedException()
    {
        return _messageLoopFailure is null
            ? new ObjectDisposedException(
                nameof(WindowsGlobalKeyboardFallbackService))
            : new InvalidOperationException(
                "The keyboard fallback message loop stopped unexpectedly.",
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

    private enum ServiceState { Active, Disposing, Disposed, Failed }

    private sealed class Registration
    {
        private int _active = 1;

        internal Registration(
            GlobalKeyboardFallbackRegistration handle,
            uint virtualKey)
        {
            Handle = handle;
            VirtualKey = virtualKey;
        }

        internal GlobalKeyboardFallbackRegistration Handle { get; }

        internal KeyboardHotkey Hotkey => Handle.Hotkey;

        internal uint VirtualKey { get; }

        internal bool IsActive => Volatile.Read(ref _active) != 0;

        internal void Deactivate()
        {
            Volatile.Write(ref _active, 0);
        }
    }

    private sealed record TriggerNotification(Registration Registration);

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
                _completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        internal void Fail(Exception exception)
        {
            if (Interlocked.Exchange(ref _claimed, 1) == 0)
            {
                _completion.TrySetException(exception);
            }
        }
    }
}
