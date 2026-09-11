using System.Collections.Concurrent;
using System.ComponentModel;
using NovaMacro.App;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MacroHotkeyCoordinatorTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);
    private static readonly KeyboardHotkey F8Hotkey =
        new(HotkeyKey.F8);

    [Fact]
    public async Task Start_F8TriggerExecutesExactlyOneAbcAndOwnsRegistration()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var delay = new ControlledDelay(events);
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, events, delay);

        try
        {
            coordinator.Start(F8Hotkey);
            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
            await AwaitAsync(delay.WaitForCallCountAsync(1));
            delay.ReleaseNext();
            await AwaitAsync(delay.WaitForCallCountAsync(2));
            delay.ReleaseNext();
            await AwaitAsync(events.WaitForCountAsync(8));

            AssertAbc(events.Snapshot());
            Assert.Collection(
                native.RegisterCalls,
                call =>
                {
                    Assert.Equal(1, call.RegistrationId);
                    Assert.Equal(0x4000u, call.Modifiers);
                    Assert.Equal(0x77u, call.VirtualKey);
                });
        }
        finally
        {
            coordinator.Dispose();
        }

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            1,
            native.PostCalls.Count(call =>
                call.Message == FakeGlobalHotkeyNativeApi.WmQuit));
    }

    [Fact]
    public async Task SecondActivationWhileMacroRuns_IsDroppedAndNotQueuedForLater()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var delay = new ControlledDelay(events);
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, events, delay);

        try
        {
            coordinator.Start(F8Hotkey);
            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
            await AwaitAsync(delay.WaitForCallCountAsync(1));

            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);

            Assert.Equal(
                new MacroEvent[]
                {
                    new KeyOutputEvent("Down", KeyboardKey.A),
                    new KeyOutputEvent("Up", KeyboardKey.A),
                    new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
                },
                events.Snapshot());

            delay.ReleaseNext();
            await AwaitAsync(delay.WaitForCallCountAsync(2));
            delay.ReleaseNext();
            await AwaitAsync(events.WaitForCountAsync(8));
        }
        finally
        {
            coordinator.Dispose();
        }

        AssertAbc(events.Snapshot());
        Assert.Equal(2, delay.CallCount);
        Assert.Equal(0, scheduler.QueuedCount);
    }

    [Fact]
    public async Task MacroFailure_IsReportedAndLaterActivationStillExecutes()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var keyboard = new RecordingKeyboardOutput(events);
        var delay = new ControlledDelay(events);
        var failure = new InvalidOperationException("controlled keyboard failure");
        var reported = new ConcurrentQueue<Exception>();
        var diagnosticObserved = NewSignal();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            keyboard,
            delay,
            exception =>
            {
                reported.Enqueue(exception);
                diagnosticObserved.TrySetResult();
                throw new InvalidOperationException("diagnostics failure");
            });

        try
        {
            coordinator.Start(F8Hotkey);
            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
            await AwaitAsync(delay.WaitForCallCountAsync(1));

            Assert.Equal(
                new MacroEvent[]
                {
                    new KeyOutputEvent("Down", KeyboardKey.A),
                    new KeyOutputEvent("Up", KeyboardKey.A),
                    new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
                },
                events.Snapshot());

            keyboard.FailNextKeyDown(failure);
            delay.ReleaseNext();
            await AwaitAsync(diagnosticObserved.Task);

            Assert.Collection(reported, exception => Assert.Same(failure, exception));
            Assert.Equal(
                new MacroEvent[]
                {
                    new KeyOutputEvent("Down", KeyboardKey.A),
                    new KeyOutputEvent("Up", KeyboardKey.A),
                    new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
                },
                events.Snapshot());

            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
            await AwaitAsync(delay.WaitForCallCountAsync(2));
            delay.ReleaseNext();
            await AwaitAsync(delay.WaitForCallCountAsync(3));
            delay.ReleaseNext();
            await AwaitAsync(events.WaitForCountAsync(11));

            Assert.Equal(
                new MacroEvent[]
                {
                    new KeyOutputEvent("Down", KeyboardKey.A),
                    new KeyOutputEvent("Up", KeyboardKey.A),
                    new DelayOutputEvent(TimeSpan.FromMilliseconds(150)),
                    new KeyOutputEvent("Down", KeyboardKey.A),
                    new KeyOutputEvent("Up", KeyboardKey.A),
                    new DelayOutputEvent(TimeSpan.FromMilliseconds(150)),
                    new KeyOutputEvent("Down", KeyboardKey.B),
                    new KeyOutputEvent("Up", KeyboardKey.B),
                    new DelayOutputEvent(TimeSpan.FromMilliseconds(150)),
                    new KeyOutputEvent("Down", KeyboardKey.C),
                    new KeyOutputEvent("Up", KeyboardKey.C)
                },
                events.Snapshot());
            Assert.Single(reported);
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task TriggerFailed_IsReportedWithoutMacroAndLaterTriggerStillWorks()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var triggerFailure = new InvalidOperationException("key-state query failure");
        var reported = new ConcurrentQueue<Exception>();
        var diagnosticObserved = NewSignal();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            events,
            new ImmediateDelay(events),
            exception =>
            {
                reported.Enqueue(exception);
                diagnosticObserved.TrySetResult();
                throw new InvalidOperationException("diagnostics failure");
            });

        try
        {
            coordinator.Start(F8Hotkey);
            native.FailNextKeyStateQuery(triggerFailure);
            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
            await AwaitAsync(diagnosticObserved.Task);

            Assert.Collection(
                reported,
                exception => Assert.Same(triggerFailure, exception));
            Assert.Empty(events.Snapshot());

            await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
            await AwaitAsync(events.WaitForCountAsync(8));

            AssertAbc(events.Snapshot());
            Assert.Single(reported);
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task FailedStart_HasNoGhostCallbackAndCanRetrySuccessfully()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            events,
            new ImmediateDelay(events));

        try
        {
            native.FailNextRegister();
            GlobalHotkeyRegistrationException failure =
                Assert.Throws<GlobalHotkeyRegistrationException>(
                () => coordinator.Start(F8Hotkey));
            Assert.Contains("Windows rejected global hotkey", failure.Message);

            native.EnqueueHotkey(registrationId: 1);
            coordinator.Start(F8Hotkey);
            Assert.Equal(0, scheduler.QueuedCount);

            await RunTriggerWorkerAsync(native, scheduler, registrationId: 2);
            await AwaitAsync(events.WaitForCountAsync(8));

            AssertAbc(events.Snapshot());
            Assert.Equal(
                new[] { 1, 2 },
                native.RegisterCalls.Select(call => call.RegistrationId));
        }
        finally
        {
            coordinator.Dispose();
        }

        Assert.Equal(new[] { 2 }, native.UnregisterCalls);
    }

    [Fact]
    public void FailedStart_CanBeDisposedWithoutGhostRegistrationOrDuplicateCleanup()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            events,
            new ImmediateDelay(events));
        native.FailNextRegister();

        Assert.Throws<GlobalHotkeyRegistrationException>(
            () => coordinator.Start(F8Hotkey));

        native.EnqueueHotkey(registrationId: 1);
        coordinator.Dispose();
        coordinator.Dispose();

        Assert.Equal(0, scheduler.QueuedCount);
        Assert.Empty(events.Snapshot());
        Assert.Empty(native.UnregisterCalls);
        Assert.Equal(
            1,
            native.PostCalls.Count(call =>
                call.Message == FakeGlobalHotkeyNativeApi.WmQuit));
    }

    [Fact]
    public async Task TriggerBeforeDisposeCutoff_IsCanceledAndDisposeWaitsForTask()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var delay = new ControlledDelay(events, holdCancellationCompletion: true);
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(service, events, delay);

        coordinator.Start(F8Hotkey);
        await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
        await AwaitAsync(delay.WaitForCallCountAsync(1));

        Task<Exception?> disposal = Task.Run(
            () => CaptureException(coordinator.Dispose));
        await AwaitAsync(delay.WaitForCancellationAsync());

        Assert.False(disposal.IsCompleted);
        Assert.Equal(
            new MacroEvent[]
            {
                new KeyOutputEvent("Down", KeyboardKey.A),
                new KeyOutputEvent("Up", KeyboardKey.A),
                new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
            },
            events.Snapshot());

        delay.CompleteCancellations();
        Assert.Null(await AwaitAsync(disposal));
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(1, delay.CancellationCount);
        Assert.Equal(
            new MacroEvent[]
            {
                new KeyOutputEvent("Down", KeyboardKey.A),
                new KeyOutputEvent("Up", KeyboardKey.A),
                new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
            },
            events.Snapshot());
    }

    [Fact]
    public async Task ComposedHotkeyServiceCutoff_PreventsLateTriggeredCallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        using var releaseFirstSubscriber = new ManualResetEventSlim(false);
        var firstSubscriberEntered = NewSignal();
        service.Triggered += _ =>
        {
            firstSubscriberEntered.TrySetResult();
            releaseFirstSubscriber.Wait();
        };
        var coordinator = CreateCoordinator(
            service,
            events,
            new ImmediateDelay(events));

        coordinator.Start(F8Hotkey);
        native.EnqueueHotkey(registrationId: 1);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        Task triggerWorker = Task.Run(scheduler.RunNextAsync);
        await AwaitAsync(firstSubscriberEntered.Task);

        Task<Exception?> disposal = Task.Run(
            () => CaptureException(coordinator.Dispose));

        try
        {
            Assert.Null(await AwaitAsync(disposal));
            Assert.Empty(events.Snapshot());
        }
        finally
        {
            releaseFirstSubscriber.Set();
        }

        await AwaitAsync(triggerWorker);
        Assert.Empty(events.Snapshot());
    }

    [Fact]
    public async Task LateCapturedTriggerFailed_AfterCoordinatorCutoffIsIgnored()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var delay = new ControlledDelay(
            events,
            holdCancellationCompletion: true,
            blockCancellationCallback: true);
        var diagnostics = new ConcurrentQueue<Exception>();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        using var releaseExternalSubscriber = new ManualResetEventSlim(false);
        var externalSubscriberEntered = NewSignal();
        service.TriggerFailed += (_, _) =>
        {
            externalSubscriberEntered.TrySetResult();
            releaseExternalSubscriber.Wait();
        };
        var coordinator = CreateCoordinator(
            service,
            events,
            delay,
            diagnostics.Enqueue);

        coordinator.Start(F8Hotkey);
        await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
        await AwaitAsync(delay.WaitForCallCountAsync(1));

        var lateFailure = new InvalidOperationException("late trigger failure");
        native.FailNextKeyStateQuery(lateFailure);
        native.EnqueueHotkey(registrationId: 1);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        Task triggerFailureWorker = Task.Run(scheduler.RunNextAsync);
        await AwaitAsync(externalSubscriberEntered.Task);

        Task<Exception?> disposal = Task.Run(
            () => CaptureException(coordinator.Dispose));
        await AwaitAsync(delay.WaitForCancellationAsync());

        Assert.False(disposal.IsCompleted);
        releaseExternalSubscriber.Set();
        await AwaitAsync(triggerFailureWorker);

        Assert.Empty(diagnostics);
        Assert.Equal(
            new MacroEvent[]
            {
                new KeyOutputEvent("Down", KeyboardKey.A),
                new KeyOutputEvent("Up", KeyboardKey.A),
                new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
            },
            events.Snapshot());

        delay.ReleaseCancellationCallback();
        Assert.False(disposal.IsCompleted);
        delay.CompleteCancellations();

        Assert.Null(await AwaitAsync(disposal));
        Assert.Empty(diagnostics);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(0, scheduler.QueuedCount);
    }

    [Fact]
    public async Task FastCompletedMacro_IsObservedAndSequentialDisposeIsSafe()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            events,
            new ImmediateDelay(events));

        coordinator.Start(F8Hotkey);
        await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
        await AwaitAsync(events.WaitForCountAsync(8));

        Task firstDispose = Task.Run(coordinator.Dispose);
        await AwaitAsync(firstDispose);
        coordinator.Dispose();

        AssertAbc(events.Snapshot());
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            1,
            native.PostCalls.Count(call =>
                call.Message == FakeGlobalHotkeyNativeApi.WmQuit));
    }

    [Fact]
    public async Task HotkeyDisposeFailure_PropagatesAfterMacroCancellationCompletes()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var events = new MacroEventLog();
        var delay = new ControlledDelay(events, holdCancellationCompletion: true);
        var diagnostics = new ConcurrentQueue<Exception>();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = CreateCoordinator(
            service,
            events,
            delay,
            diagnostics.Enqueue);

        coordinator.Start(F8Hotkey);
        await RunTriggerWorkerAsync(native, scheduler, registrationId: 1);
        await AwaitAsync(delay.WaitForCallCountAsync(1));
        native.FailNextUnregister();

        Task<Exception?> disposal = Task.Run(
            () => CaptureException(coordinator.Dispose));
        await AwaitAsync(delay.WaitForCancellationAsync());

        Assert.False(disposal.IsCompleted);
        delay.CompleteCancellations();

        Win32Exception failure = Assert.IsType<Win32Exception>(
            await AwaitAsync(disposal));
        Assert.Contains("Failed to unregister", failure.Message);
        Assert.Equal(1, delay.CancellationCount);
        Assert.Empty(diagnostics);
        Assert.Equal(
            new MacroEvent[]
            {
                new KeyOutputEvent("Down", KeyboardKey.A),
                new KeyOutputEvent("Up", KeyboardKey.A),
                new DelayOutputEvent(TimeSpan.FromMilliseconds(150))
            },
            events.Snapshot());
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);

        coordinator.Dispose();
    }

    private static MacroHotkeyCoordinator CreateCoordinator(
        WindowsGlobalHotkeyService service,
        MacroEventLog events,
        IDelay delay,
        Action<Exception>? reportRuntimeFailure = null)
    {
        return CreateCoordinator(
            service,
            new RecordingKeyboardOutput(events),
            delay,
            reportRuntimeFailure);
    }

    private static MacroHotkeyCoordinator CreateCoordinator(
        WindowsGlobalHotkeyService service,
        IKeyboardOutput keyboard,
        IDelay delay,
        Action<Exception>? reportRuntimeFailure = null)
    {
        return new MacroHotkeyCoordinator(
            service,
            new MacroRunner(keyboard, delay),
            BuiltInMacroSequences.AbcTest,
            reportRuntimeFailure ?? (_ => { }));
    }

    private static async Task RunTriggerWorkerAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler,
        int registrationId)
    {
        int expectedCount = scheduler.QueuedCount + 1;
        native.EnqueueHotkey(registrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(expectedCount));
        await AwaitAsync(scheduler.RunNextAsync());
    }

    private static void AssertAbc(IReadOnlyList<MacroEvent> events)
    {
        Assert.Equal(
            new MacroEvent[]
            {
                new KeyOutputEvent("Down", KeyboardKey.A),
                new KeyOutputEvent("Up", KeyboardKey.A),
                new DelayOutputEvent(TimeSpan.FromMilliseconds(150)),
                new KeyOutputEvent("Down", KeyboardKey.B),
                new KeyOutputEvent("Up", KeyboardKey.B),
                new DelayOutputEvent(TimeSpan.FromMilliseconds(150)),
                new KeyOutputEvent("Down", KeyboardKey.C),
                new KeyOutputEvent("Up", KeyboardKey.C)
            },
            events);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(
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

    private static async Task AwaitAsync(Task task)
    {
        await task.WaitAsync(SafetyTimeout);
    }

    private static async Task<T> AwaitAsync<T>(Task<T> task)
    {
        return await task.WaitAsync(SafetyTimeout);
    }

    private abstract record MacroEvent;

    private sealed record KeyOutputEvent(
        string Action,
        KeyboardKey Key) : MacroEvent;

    private sealed record DelayOutputEvent(TimeSpan Duration) : MacroEvent;

    private sealed class MacroEventLog
    {
        private readonly object _gate = new();
        private readonly List<MacroEvent> _events = new();
        private readonly List<EventCountWaiter> _waiters = new();

        internal void Add(MacroEvent macroEvent)
        {
            List<TaskCompletionSource>? completed = null;

            lock (_gate)
            {
                _events.Add(macroEvent);

                for (int index = _waiters.Count - 1; index >= 0; index--)
                {
                    EventCountWaiter waiter = _waiters[index];
                    if (_events.Count >= waiter.ExpectedCount)
                    {
                        completed ??= new List<TaskCompletionSource>();
                        completed.Add(waiter.Completion);
                        _waiters.RemoveAt(index);
                    }
                }
            }

            if (completed is not null)
            {
                foreach (TaskCompletionSource completion in completed)
                {
                    completion.TrySetResult();
                }
            }
        }

        internal IReadOnlyList<MacroEvent> Snapshot()
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }

        internal Task WaitForCountAsync(int expectedCount)
        {
            lock (_gate)
            {
                if (_events.Count >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                var completion = NewSignal();
                _waiters.Add(new EventCountWaiter(expectedCount, completion));
                return completion.Task;
            }
        }

        private sealed record EventCountWaiter(
            int ExpectedCount,
            TaskCompletionSource Completion);
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly object _gate = new();
        private readonly MacroEventLog _events;
        private Exception? _nextKeyDownFailure;

        internal RecordingKeyboardOutput(MacroEventLog events)
        {
            _events = events;
        }

        internal void FailNextKeyDown(Exception exception)
        {
            lock (_gate)
            {
                _nextKeyDownFailure = exception;
            }
        }

        public void KeyDown(KeyboardKey key)
        {
            Exception? failure;

            lock (_gate)
            {
                failure = _nextKeyDownFailure;
                _nextKeyDownFailure = null;
            }

            if (failure is not null)
            {
                throw failure;
            }

            _events.Add(new KeyOutputEvent("Down", key));
        }

        public void KeyUp(KeyboardKey key)
        {
            _events.Add(new KeyOutputEvent("Up", key));
        }
    }

    private sealed class ImmediateDelay : IDelay
    {
        private readonly MacroEventLog _events;

        internal ImmediateDelay(MacroEventLog events)
        {
            _events = events;
        }

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Add(new DelayOutputEvent(duration));
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledDelay : IDelay
    {
        private readonly object _gate = new();
        private readonly MacroEventLog _events;
        private readonly bool _holdCancellationCompletion;
        private readonly ManualResetEventSlim? _cancellationCallbackRelease;
        private readonly Queue<PendingDelay> _pending = new();
        private readonly List<DelayCountWaiter> _countWaiters = new();
        private readonly TaskCompletionSource _cancellationObserved = NewSignal();
        private int _callCount;
        private int _cancellationCount;

        internal ControlledDelay(
            MacroEventLog events,
            bool holdCancellationCompletion = false,
            bool blockCancellationCallback = false)
        {
            _events = events;
            _holdCancellationCompletion = holdCancellationCompletion;
            _cancellationCallbackRelease = blockCancellationCallback
                ? new ManualResetEventSlim(false)
                : null;
        }

        internal int CallCount => Volatile.Read(ref _callCount);

        internal int CancellationCount => Volatile.Read(ref _cancellationCount);

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            var pending = new PendingDelay(
                cancellationToken,
                _holdCancellationCompletion,
                OnCancellationObserved);
            List<TaskCompletionSource>? completed = null;

            lock (_gate)
            {
                _pending.Enqueue(pending);
                _callCount++;

                for (int index = _countWaiters.Count - 1; index >= 0; index--)
                {
                    DelayCountWaiter waiter = _countWaiters[index];
                    if (_callCount >= waiter.ExpectedCount)
                    {
                        completed ??= new List<TaskCompletionSource>();
                        completed.Add(waiter.Completion);
                        _countWaiters.RemoveAt(index);
                    }
                }
            }

            _events.Add(new DelayOutputEvent(duration));

            if (completed is not null)
            {
                foreach (TaskCompletionSource completion in completed)
                {
                    completion.TrySetResult();
                }
            }

            return pending.WaitAsync();
        }

        internal Task WaitForCallCountAsync(int expectedCount)
        {
            lock (_gate)
            {
                if (_callCount >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                var completion = NewSignal();
                _countWaiters.Add(new DelayCountWaiter(expectedCount, completion));
                return completion.Task;
            }
        }

        internal Task WaitForCancellationAsync()
        {
            return _cancellationObserved.Task;
        }

        internal void ReleaseNext()
        {
            while (true)
            {
                PendingDelay pending;

                lock (_gate)
                {
                    if (!_pending.TryDequeue(out pending!))
                    {
                        throw new InvalidOperationException("No delay is pending.");
                    }
                }

                if (pending.TryRelease())
                {
                    return;
                }
            }
        }

        internal void CompleteCancellations()
        {
            PendingDelay[] pending;

            lock (_gate)
            {
                pending = _pending.ToArray();
            }

            foreach (PendingDelay delay in pending)
            {
                delay.CompleteCancellation();
            }
        }

        internal void ReleaseCancellationCallback()
        {
            _cancellationCallbackRelease?.Set();
        }

        private void OnCancellationObserved()
        {
            Interlocked.Increment(ref _cancellationCount);
            _cancellationObserved.TrySetResult();
            _cancellationCallbackRelease?.Wait();
        }

        private sealed record DelayCountWaiter(
            int ExpectedCount,
            TaskCompletionSource Completion);

        private sealed class PendingDelay
        {
            private readonly object _gate = new();
            private readonly CancellationToken _cancellationToken;
            private readonly bool _holdCancellationCompletion;
            private readonly Action _onCancellationObserved;
            private readonly TaskCompletionSource _completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private bool _cancellationRequested;

            internal PendingDelay(
                CancellationToken cancellationToken,
                bool holdCancellationCompletion,
                Action onCancellationObserved)
            {
                _cancellationToken = cancellationToken;
                _holdCancellationCompletion = holdCancellationCompletion;
                _onCancellationObserved = onCancellationObserved;
            }

            internal async Task WaitAsync()
            {
                using CancellationTokenRegistration registration =
                    _cancellationToken.Register(Cancel);
                await _completion.Task.ConfigureAwait(false);
            }

            internal bool TryRelease()
            {
                lock (_gate)
                {
                    if (_cancellationRequested)
                    {
                        return false;
                    }

                    return _completion.TrySetResult();
                }
            }

            internal void CompleteCancellation()
            {
                lock (_gate)
                {
                    if (_cancellationRequested)
                    {
                        _completion.TrySetCanceled(_cancellationToken);
                    }
                }
            }

            private void Cancel()
            {
                lock (_gate)
                {
                    if (_cancellationRequested)
                    {
                        return;
                    }

                    _cancellationRequested = true;
                }

                _onCancellationObserved();

                if (!_holdCancellationCompletion)
                {
                    _completion.TrySetCanceled(_cancellationToken);
                }
            }
        }
    }
}
