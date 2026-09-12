using System.Collections.Concurrent;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MultiMacroHotkeyCoordinatorLifecycleTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task TerminalPhase1BeforeCancellation_RejectsPendingStartWithoutOutput()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboard = new RecordingKeyboardOutput();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var terminalPhase1Reached = NewSignal();
        using var releaseTerminal = new ManualResetEventSlim(false);
        var coordinator = CreateCoordinator(
            service,
            keyboard,
            () =>
            {
                terminalPhase1Reached.TrySetResult();
                releaseTerminal.Wait();
            });
        coordinator.Start();
        var terminalStatus = NewSignal<MultiMacroRuntimeStatus>();
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.ServiceUnavailable)
            {
                terminalStatus.TrySetResult(status);
            }
        };

        native.EnqueueHotkey(registrationId: 1);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);

        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(queuedContext);
        Task triggerWorker;

        try
        {
            triggerWorker = scheduler.RunNextAsync();
            Assert.True(triggerWorker.IsCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        await triggerWorker;
        Assert.Equal(1, queuedContext.QueuedCount);

        try
        {
            native.FailNextGetMessage();
            await terminalPhase1Reached.Task.WaitAsync(SafetyTimeout);
            Assert.False(terminalStatus.Task.IsCompleted);

            queuedContext.RunAll();

            Assert.Equal(0, keyboard.CallCount);
        }
        finally
        {
            releaseTerminal.Set();
        }

        await terminalStatus.Task.WaitAsync(SafetyTimeout);

        Assert.Equal(0, keyboard.CallCount);
        Assert.IsType<System.ComponentModel.Win32Exception>(
            CaptureException(coordinator.Dispose));
    }

    [Fact]
    public async Task ExecutionStartHandshakeBeforeTerminal_AllowsStartedKeyTapWithoutHoldingStateLock()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboard = new BlockingKeyboardOutput();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, keyboard);
        coordinator.Start();
        var terminalStatus = NewSignal<MultiMacroRuntimeStatus>();
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.ServiceUnavailable)
            {
                terminalStatus.TrySetResult(status);
            }
        };

        await RunTriggerAsync(native, scheduler);
        await keyboard.KeyDownEntered.Task.WaitAsync(SafetyTimeout);

        try
        {
            native.FailNextGetMessage();
            MultiMacroRuntimeStatus status = await terminalStatus.Task
                .WaitAsync(SafetyTimeout);

            Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, status.State);
            Assert.False(keyboard.KeyDownFinished);
        }
        finally
        {
            keyboard.ReleaseKeyDown();
        }

        Assert.IsType<System.ComponentModel.Win32Exception>(
            CaptureException(coordinator.Dispose));
    }

    [Fact]
    public async Task ExecutionFailedSubscriber_CanDisposeWithoutSelfWait()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var failure = new InvalidOperationException("controlled output failure");
        var keyboard = new RecordingKeyboardOutput(failure);
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, keyboard);
        var callbackFinished = NewSignal();
        Exception? observedFailure = null;
        Exception? disposalFailure = null;
        var lateCallbackCount = 0;
        coordinator.ExecutionFailed += (_, exception) =>
        {
            observedFailure = exception;
            disposalFailure = CaptureException(coordinator.Dispose);
            callbackFinished.TrySetResult();
        };
        coordinator.ExecutionFailed += (_, _) =>
            Interlocked.Increment(ref lateCallbackCount);
        coordinator.Start();

        await RunTriggerAsync(native, scheduler);
        await callbackFinished.Task.WaitAsync(SafetyTimeout);

        Assert.Same(failure, observedFailure);
        Assert.Null(disposalFailure);
        Assert.Equal(0, Volatile.Read(ref lateCallbackCount));
        coordinator.Dispose();
    }

    [Fact]
    public async Task TriggerFailedSubscriberDispose_ClosesLaterNotificationAdmission()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboard = new RecordingKeyboardOutput();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, keyboard);
        var triggerFailure = new InvalidOperationException(
            "controlled release-query failure");
        var callbackFinished = NewSignal();
        Exception? observedFailure = null;
        Exception? disposalFailure = null;
        var lateCallbackCount = 0;
        coordinator.ExecutionFailed += (_, exception) =>
        {
            observedFailure = exception;
            disposalFailure = CaptureException(coordinator.Dispose);
            callbackFinished.TrySetResult();
        };
        coordinator.ExecutionFailed += (_, _) =>
            Interlocked.Increment(ref lateCallbackCount);
        coordinator.Start();
        native.FailNextKeyStateQuery(triggerFailure);

        await RunTriggerAsync(native, scheduler);
        await callbackFinished.Task.WaitAsync(SafetyTimeout);

        Assert.Same(triggerFailure, observedFailure);
        Assert.Null(disposalFailure);
        Assert.Equal(0, Volatile.Read(ref lateCallbackCount));
        Assert.Equal(0, keyboard.CallCount);
        coordinator.Dispose();
    }

    [Fact]
    public async Task TerminalStatusSubscriberDispose_ClosesLaterNotificationAdmission()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            new RecordingKeyboardOutput());
        coordinator.Start();
        var callbackFinished = NewSignal();
        MultiMacroRuntimeStatus? observedStatus = null;
        Exception? disposalFailure = null;
        var lateCallbackCount = 0;
        coordinator.StatusChanged += status =>
        {
            observedStatus = status;
            disposalFailure = CaptureException(coordinator.Dispose);
            callbackFinished.TrySetResult();
        };
        coordinator.StatusChanged += _ =>
            Interlocked.Increment(ref lateCallbackCount);

        native.FailNextGetMessage();
        await callbackFinished.Task.WaitAsync(SafetyTimeout);

        Assert.Equal(
            MultiMacroRuntimeState.ServiceUnavailable,
            observedStatus?.State);
        Assert.IsType<System.ComponentModel.Win32Exception>(disposalFailure);
        Assert.Equal(0, Volatile.Read(ref lateCallbackCount));
        Assert.Equal(
            MultiMacroRuntimeState.ServiceUnavailable,
            coordinator.CurrentStatus?.State);
        coordinator.Dispose();
    }

    [Fact]
    public async Task TerminalDuringActiveDelay_CancelsLaterStepsWithoutExecutionFailure()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboard = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.A),
                new DelayStep(TimeSpan.FromMilliseconds(1)),
                new KeyTapStep(KeyboardKey.B)
            });
        var coordinator = CreateCoordinator(service, keyboard, delay, sequence);
        var terminalStatus = NewSignal<MultiMacroRuntimeStatus>();
        var executionFailures = new ConcurrentQueue<Exception>();
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.ServiceUnavailable)
            {
                terminalStatus.TrySetResult(status);
            }
        };
        coordinator.ExecutionFailed += (_, exception) =>
            executionFailures.Enqueue(exception);
        coordinator.Start();

        await RunTriggerAsync(native, scheduler);
        await delay.Entered.Task.WaitAsync(SafetyTimeout);
        Assert.Equal(2, keyboard.CallCount);

        native.FailNextGetMessage();
        MultiMacroRuntimeStatus status = await terminalStatus.Task.WaitAsync(
            SafetyTimeout);
        await delay.CancellationObserved.Task.WaitAsync(SafetyTimeout);

        Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, status.State);
        Assert.Equal(2, keyboard.CallCount);
        Assert.Empty(executionFailures);
        Assert.IsType<System.ComponentModel.Win32Exception>(
            CaptureException(coordinator.Dispose));
    }

    [Fact]
    public async Task DisposeDuringPendingStart_RejectsSecondHandshakeWithoutOutput()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboard = new RecordingKeyboardOutput();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, keyboard);
        coordinator.Start();
        var statusCallbackCount = 0;
        var executionFailureCallbackCount = 0;
        coordinator.StatusChanged += _ => Interlocked.Increment(
            ref statusCallbackCount);
        coordinator.ExecutionFailed += (_, _) => Interlocked.Increment(
            ref executionFailureCallbackCount);
        native.EnqueueHotkey(registrationId: 1);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        Task triggerWorker;

        try
        {
            SynchronizationContext.SetSynchronizationContext(queuedContext);
            triggerWorker = scheduler.RunNextAsync();
            Assert.True(triggerWorker.IsCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        await triggerWorker;
        Assert.Equal(1, queuedContext.QueuedCount);

        Task<Exception?> disposal = Task.Run(
            () => CaptureException(coordinator.Dispose));
        await native.WaitForPostCountAsync(2).WaitAsync(SafetyTimeout);

        queuedContext.RunAll();

        Assert.Null(await disposal.WaitAsync(SafetyTimeout));
        Assert.Equal(0, keyboard.CallCount);
        Assert.Equal(0, Volatile.Read(ref statusCallbackCount));
        Assert.Equal(0, Volatile.Read(ref executionFailureCallbackCount));
    }

    [Fact]
    public async Task DisposeDuringStartedExecution_CancelsLaterStepsWithoutExecutionFailure()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboard = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.A),
                new DelayStep(TimeSpan.FromMilliseconds(1)),
                new KeyTapStep(KeyboardKey.B)
            });
        var coordinator = CreateCoordinator(service, keyboard, delay, sequence);
        var executionFailures = new ConcurrentQueue<Exception>();
        coordinator.ExecutionFailed += (_, exception) =>
            executionFailures.Enqueue(exception);
        coordinator.Start();

        await RunTriggerAsync(native, scheduler);
        await delay.Entered.Task.WaitAsync(SafetyTimeout);
        Assert.Equal(2, keyboard.CallCount);

        Task<Exception?> disposal = Task.Run(
            () => CaptureException(coordinator.Dispose));
        await delay.CancellationObserved.Task.WaitAsync(SafetyTimeout);

        Assert.Null(await disposal.WaitAsync(SafetyTimeout));
        Assert.Equal(2, keyboard.CallCount);
        Assert.Empty(executionFailures);
    }

    [Fact]
    public async Task TerminalAndDisposeRace_DoesNotResurrectStatusOrAdmitLaterNotifications()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var terminalPhase1Reached = NewSignal();
        using var releaseTerminal = new ManualResetEventSlim(false);
        var coordinator = CreateCoordinator(
            service,
            new RecordingKeyboardOutput(),
            () =>
            {
                terminalPhase1Reached.TrySetResult();
                releaseTerminal.Wait();
            });
        var statusEvents = new ConcurrentQueue<MultiMacroRuntimeStatus>();
        coordinator.Start();
        coordinator.StatusChanged += statusEvents.Enqueue;

        native.FailNextGetMessage();
        await terminalPhase1Reached.Task.WaitAsync(SafetyTimeout);
        var disposeEntered = NewSignal();
        Task<Exception?> disposal = Task.Run(() =>
        {
            disposeEntered.TrySetResult();
            return CaptureException(coordinator.Dispose);
        });
        await disposeEntered.Task.WaitAsync(SafetyTimeout);

        releaseTerminal.Set();

        Exception? failure = await disposal.WaitAsync(SafetyTimeout);
        Assert.IsType<System.ComponentModel.Win32Exception>(failure);
        MultiMacroRuntimeStatus terminal = Assert.Single(statusEvents);
        Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, terminal.State);
        Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, coordinator.CurrentStatus?.State);

        native.EnqueueHotkey(registrationId: 1);
        Assert.Equal(0, scheduler.QueuedCount);
        Assert.Single(statusEvents);
    }

    private static MultiMacroHotkeyCoordinator CreateCoordinator(
        WindowsGlobalHotkeyService service,
        IKeyboardOutput keyboard,
        Action? terminalPhase1CompletedForTesting = null)
    {
        return CreateCoordinator(
            service,
            keyboard,
            new ImmediateDelay(),
            new MacroSequence(
                new MacroStep[] { new KeyTapStep(KeyboardKey.A) }),
            terminalPhase1CompletedForTesting);
    }

    private static MultiMacroHotkeyCoordinator CreateCoordinator(
        WindowsGlobalHotkeyService service,
        IKeyboardOutput keyboard,
        IDelay delay,
        MacroSequence sequence,
        Action? terminalPhase1CompletedForTesting = null)
    {
        var settings = new ApplicationSettings(
            new[]
            {
                new MacroDefinition(
                    new MacroId(new Guid("11111111-1111-1111-1111-111111111111")),
                    "Runtime test macro",
                    true,
                    new KeyboardHotkey(HotkeyKey.F8),
                    sequence)
            });

        return new MultiMacroHotkeyCoordinator(
            settings,
            () => service,
            new MacroRunner(keyboard, delay),
            terminalPhase1CompletedForTesting);
    }

    private static async Task RunTriggerAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler)
    {
        native.EnqueueHotkey(registrationId: 1);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await scheduler.RunNextAsync().WaitAsync(SafetyTimeout);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<T> NewSignal<T>()
    {
        return new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly Exception? _keyDownFailure;
        private int _callCount;

        internal RecordingKeyboardOutput(Exception? keyDownFailure = null)
        {
            _keyDownFailure = keyDownFailure;
        }

        internal int CallCount => Volatile.Read(ref _callCount);

        public void KeyDown(KeyboardKey key)
        {
            Interlocked.Increment(ref _callCount);

            if (_keyDownFailure is not null)
            {
                throw _keyDownFailure;
            }
        }

        public void KeyUp(KeyboardKey key)
        {
            Interlocked.Increment(ref _callCount);
        }
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledDelay : IDelay
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Entered { get; } = NewSignal();

        internal TaskCompletionSource CancellationObserved { get; } = NewSignal();

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            cancellationToken.Register(
                () => CancellationObserved.TrySetResult());
            return _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingKeyboardOutput : IKeyboardOutput
    {
        private readonly ManualResetEventSlim _releaseKeyDown = new(false);
        private int _keyDownFinished;

        internal TaskCompletionSource KeyDownEntered { get; } = NewSignal();

        internal bool KeyDownFinished =>
            Volatile.Read(ref _keyDownFinished) != 0;

        public void KeyDown(KeyboardKey key)
        {
            KeyDownEntered.TrySetResult();
            _releaseKeyDown.Wait();
            Volatile.Write(ref _keyDownFinished, 1);
        }

        public void KeyUp(KeyboardKey key)
        {
        }

        internal void ReleaseKeyDown()
        {
            _releaseKeyDown.Set();
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)>
            _callbacks = new();

        internal int QueuedCount => _callbacks.Count;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue((callback, state));
        }

        internal void RunAll()
        {
            while (_callbacks.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
    }
}
