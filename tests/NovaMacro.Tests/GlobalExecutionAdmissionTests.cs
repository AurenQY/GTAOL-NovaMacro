using System.Collections.Concurrent;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class GlobalExecutionAdmissionTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    public static TheoryData<TriggerKind, TriggerKind> BusyPairings => new()
    {
        { TriggerKind.KeyboardOnce, TriggerKind.KeyboardOnce },
        { TriggerKind.KeyboardOnce, TriggerKind.MouseOnce },
        { TriggerKind.KeyboardOnce, TriggerKind.MouseHeld },
        { TriggerKind.MouseOnce, TriggerKind.KeyboardOnce },
        { TriggerKind.MouseOnce, TriggerKind.MouseOnce },
        { TriggerKind.MouseOnce, TriggerKind.MouseHeld },
        { TriggerKind.MouseHeld, TriggerKind.KeyboardOnce },
        { TriggerKind.MouseHeld, TriggerKind.MouseOnce },
        { TriggerKind.MouseHeld, TriggerKind.MouseHeld }
    };

    [Theory]
    [MemberData(nameof(BusyPairings))]
    public async Task SecondTriggerWhileOwnerActiveIsDroppedBeforeWorkerScheduling(
        TriggerKind first,
        TriggerKind second)
    {
        using var harness = new AdmissionHarness(first, second);
        harness.Start();

        await harness.StartFirstAsync();
        await harness.Delay.Entered.Task.WaitAsync(SafetyTimeout);

        QueuedSynchronizationContext queuedContext =
            await harness.TriggerSecondHoldingCoordinatorWorkersAsync();

        Assert.Equal(0, queuedContext.QueuedCount);

        await harness.ReleaseFirstMouseButtonAsync();
        harness.Delay.Release();
        await harness.Output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);

        queuedContext.RunAll();
        await harness.Coordinator.WaitForIdleForTestingAsync().WaitAsync(
            SafetyTimeout);

        Assert.Equal(
            new[] { "Down:A", "Up:A", "Down:B", "Up:B" },
            harness.Output.Snapshot());
    }

    [Fact]
    public async Task RejectedMousePressMustBeReleasedBeforeANewPressCanExecute()
    {
        using var harness = new AdmissionHarness(
            TriggerKind.MouseOnce,
            TriggerKind.MouseOnce);
        harness.Start();

        await harness.StartFirstAsync();
        await harness.Delay.Entered.Task.WaitAsync(SafetyTimeout);
        QueuedSynchronizationContext queuedContext =
            await harness.TriggerSecondHoldingCoordinatorWorkersAsync();
        Assert.Equal(0, queuedContext.QueuedCount);

        await harness.ReleaseFirstMouseButtonAsync();
        harness.Delay.Release();
        await harness.Coordinator.WaitForIdleForTestingAsync().WaitAsync(
            SafetyTimeout);

        await harness.TriggerSecondNormallyAsync();
        await harness.Coordinator.WaitForIdleForTestingAsync().WaitAsync(
            SafetyTimeout);

        Assert.Equal(
            new[]
            {
                "Down:A", "Up:A", "Down:B", "Up:B",
                "Down:C", "Up:C"
            },
            harness.Output.Snapshot());
    }

    [Fact]
    public async Task StaleFailedCompletionCannotClearNewerExecutionOwner()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        using var allowFirstCompletion = new ManualResetEventSlim();
        var firstOwnerReleased = NewSignal();
        int releaseCount = 0;
        var expectedFailure = new InvalidOperationException("controlled failure");
        var output = new FailFirstKeyboardOutput(expectedFailure);
        var delay = new ControlledDelay();
        var observedFailure = new TaskCompletionSource<(MacroId Id, Exception Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new MultiMacroHotkeyCoordinator(
            new ApplicationSettings(new[]
            {
                KeyboardMacro(1, HotkeyKey.F8, KeySequence(KeyboardKey.A)),
                KeyboardMacro(
                    2,
                    HotkeyKey.F9,
                    new MacroSequence(new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.B),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.C)
                    })),
                KeyboardMacro(3, HotkeyKey.F10, KeySequence(KeyboardKey.D))
            }),
            () => service,
            new MacroRunner(output, new NoOpMouseOutput(), delay),
            executionOwnerReleasedForTesting: () =>
            {
                if (Interlocked.Increment(ref releaseCount) == 1)
                {
                    firstOwnerReleased.TrySetResult();
                    allowFirstCompletion.Wait(SafetyTimeout);
                }
            });
        coordinator.ExecutionFailed += (id, error) =>
            observedFailure.TrySetResult((id, error));

        try
        {
            coordinator.Start();
            await TriggerKeyboardAsync(native, scheduler, HotkeyKey.F8);
            await firstOwnerReleased.Task.WaitAsync(SafetyTimeout);

            await TriggerKeyboardAsync(native, scheduler, HotkeyKey.F9);
            await delay.Entered.Task.WaitAsync(SafetyTimeout);

            allowFirstCompletion.Set();
            (MacroId Id, Exception Error) failure =
                await observedFailure.Task.WaitAsync(SafetyTimeout);
            Assert.Equal(Id(1), failure.Id);
            Assert.Same(expectedFailure, failure.Error);

            QueuedSynchronizationContext queuedContext =
                await TriggerKeyboardHoldingCoordinatorWorkersAsync(
                    native,
                    scheduler,
                    HotkeyKey.F10);
            Assert.Equal(0, queuedContext.QueuedCount);

            delay.Release();
            await output.WaitForCallCountAsync(5).WaitAsync(SafetyTimeout);
            queuedContext.RunAll();
            await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

            Assert.Equal(
                new[]
                {
                    "Down:A",
                    "Down:B", "Up:B",
                    "Down:C", "Up:C"
                },
                output.Snapshot());
        }
        finally
        {
            allowFirstCompletion.Set();
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task WorkerSchedulingFailureReleasesOwnerAndReportsExecutionFailure()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = new MultiMacroHotkeyCoordinator(
            new ApplicationSettings(new[]
            {
                KeyboardMacro(1, HotkeyKey.F8, KeySequence(KeyboardKey.A)),
                KeyboardMacro(2, HotkeyKey.F9, KeySequence(KeyboardKey.B))
            }),
            () => service,
            new MacroRunner(output, new NoOpMouseOutput(), new ImmediateDelay()));
        var expectedFailure = new InvalidOperationException(
            "controlled scheduling failure");
        var observedFailure = new TaskCompletionSource<(MacroId Id, Exception Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.ExecutionFailed += (id, error) =>
            observedFailure.TrySetResult((id, error));
        coordinator.Start();

        int firstRegistration = FindRegistrationId(native, HotkeyKey.F8);
        native.EnqueueHotkey(firstRegistration);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task worker;
        try
        {
            SynchronizationContext.SetSynchronizationContext(
                new ThrowingSynchronizationContext(expectedFailure));
            worker = scheduler.RunNextAsync();
            Assert.True(worker.IsCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        await worker;

        (MacroId Id, Exception Error) failure =
            await observedFailure.Task.WaitAsync(SafetyTimeout);
        Assert.Equal(Id(1), failure.Id);
        Assert.Same(expectedFailure, failure.Error);

        await TriggerKeyboardAsync(native, scheduler, HotkeyKey.F9);
        await output.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(new[] { "Down:B", "Up:B" }, output.Snapshot());
    }

    public enum TriggerKind
    {
        KeyboardOnce,
        MouseOnce,
        MouseHeld
    }

    private static MacroDefinition KeyboardMacro(
        int id,
        HotkeyKey key,
        MacroSequence sequence) => new(
        Id(id),
        $"Keyboard {id}",
        enabled: true,
        new KeyboardMacroTrigger(new KeyboardHotkey(key)),
        MacroExecutionMode.Once,
        sequence);

    private static MacroSequence KeySequence(KeyboardKey key) =>
        new(new MacroStep[] { new KeyTapStep(key) });

    private static MacroId Id(int value) =>
        new(new Guid(value, 0, 0, new byte[8]));

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task TriggerKeyboardAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler,
        HotkeyKey key)
    {
        int registrationId = FindRegistrationId(native, key);
        native.EnqueueHotkey(registrationId);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await scheduler.RunNextAsync();
    }

    private static async Task<QueuedSynchronizationContext>
        TriggerKeyboardHoldingCoordinatorWorkersAsync(
            FakeGlobalHotkeyNativeApi native,
            FakeHotkeyTriggerWorkScheduler scheduler,
            HotkeyKey key)
    {
        int registrationId = FindRegistrationId(native, key);
        native.EnqueueHotkey(registrationId);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queuedContext);
            await scheduler.RunNextAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        return queuedContext;
    }

    private static int FindRegistrationId(
        FakeGlobalHotkeyNativeApi native,
        HotkeyKey key)
    {
        uint virtualKey = key switch
        {
            HotkeyKey.F8 => 0x77,
            HotkeyKey.F9 => 0x78,
            HotkeyKey.F10 => 0x79,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        return native.RegisterCalls
            .Single(call => call.VirtualKey == virtualKey)
            .RegistrationId;
    }

    private sealed class AdmissionHarness : IDisposable
    {
        private readonly TriggerKind _first;
        private readonly TriggerKind _second;
        private readonly FakeGlobalHotkeyNativeApi _keyboardNative = new();
        private readonly FakeHotkeyTriggerWorkScheduler _keyboardScheduler = new();
        private readonly WindowsGlobalHotkeyService _keyboardService;
        private readonly FakeGlobalMouseButtonNativeApi _mouseNative = new();
        private readonly FakeHotkeyTriggerWorkScheduler _mouseScheduler = new();
        private readonly WindowsGlobalMouseButtonService _mouseService;

        internal AdmissionHarness(TriggerKind first, TriggerKind second)
        {
            _first = first;
            _second = second;
            _keyboardService = new WindowsGlobalHotkeyService(
                _keyboardNative,
                _keyboardScheduler);
            _mouseService = new WindowsGlobalMouseButtonService(
                _mouseNative,
                _mouseScheduler);
            Output = new RecordingKeyboardOutput();
            Delay = new ControlledDelay();
            Coordinator = new MultiMacroHotkeyCoordinator(
                new ApplicationSettings(new[]
                {
                    CreateMacro(first, isFirst: true),
                    CreateMacro(second, isFirst: false)
                }),
                () => _keyboardService,
                new MacroRunner(Output, new NoOpMouseOutput(), Delay),
                mouseButtonServiceFactory: () => _mouseService);
        }

        internal MultiMacroHotkeyCoordinator Coordinator { get; }

        internal RecordingKeyboardOutput Output { get; }

        internal ControlledDelay Delay { get; }

        internal void Start() => Coordinator.Start();

        internal async Task StartFirstAsync()
        {
            if (_first == TriggerKind.KeyboardOnce)
            {
                await TriggerKeyboardAsync(
                    _keyboardNative,
                    _keyboardScheduler,
                    HotkeyKey.F8);
                return;
            }

            _mouseNative.InvokeHook(
                FakeGlobalMouseButtonNativeApi.WmXButtonDown,
                xButton: 2);
            await _mouseScheduler.DrainAsync();
        }

        internal async Task<QueuedSynchronizationContext>
            TriggerSecondHoldingCoordinatorWorkersAsync()
        {
            if (_second == TriggerKind.KeyboardOnce)
            {
                return await TriggerKeyboardHoldingCoordinatorWorkersAsync(
                    _keyboardNative,
                    _keyboardScheduler,
                    HotkeyKey.F9);
            }

            _mouseNative.InvokeHook(
                FakeGlobalMouseButtonNativeApi.WmXButtonDown,
                xButton: 1);
            _mouseNative.InvokeHook(
                FakeGlobalMouseButtonNativeApi.WmXButtonUp,
                xButton: 1);
            var queuedContext = new QueuedSynchronizationContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(queuedContext);
                await _mouseScheduler.DrainAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            return queuedContext;
        }

        internal async Task ReleaseFirstMouseButtonAsync()
        {
            if (_first == TriggerKind.KeyboardOnce)
            {
                return;
            }

            _mouseNative.InvokeHook(
                FakeGlobalMouseButtonNativeApi.WmXButtonUp,
                xButton: 2);
            await _mouseScheduler.DrainAsync();
        }

        internal async Task TriggerSecondNormallyAsync()
        {
            if (_second == TriggerKind.KeyboardOnce)
            {
                await TriggerKeyboardAsync(
                    _keyboardNative,
                    _keyboardScheduler,
                    HotkeyKey.F9);
                return;
            }

            _mouseNative.InvokeHook(
                FakeGlobalMouseButtonNativeApi.WmXButtonDown,
                xButton: 1);
            _mouseNative.InvokeHook(
                FakeGlobalMouseButtonNativeApi.WmXButtonUp,
                xButton: 1);
            await _mouseScheduler.DrainAsync();
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            _keyboardService.Dispose();
            _mouseService.Dispose();
            _keyboardNative.Dispose();
            _mouseNative.Dispose();
        }

        private static MacroDefinition CreateMacro(
            TriggerKind kind,
            bool isFirst)
        {
            MacroTrigger trigger = kind == TriggerKind.KeyboardOnce
                ? new KeyboardMacroTrigger(new KeyboardHotkey(
                    isFirst ? HotkeyKey.F8 : HotkeyKey.F9))
                : new MouseButtonMacroTrigger(
                    isFirst ? MouseButton.XButton2 : MouseButton.XButton1);
            MacroExecutionMode mode = kind == TriggerKind.MouseHeld
                ? MacroExecutionMode.RepeatWhileHeld
                : MacroExecutionMode.Once;
            MacroSequence sequence = isFirst
                ? new MacroSequence(new MacroStep[]
                {
                    new KeyTapStep(KeyboardKey.A),
                    new DelayStep(TimeSpan.FromMilliseconds(1)),
                    new KeyTapStep(KeyboardKey.B)
                })
                : KeySequence(KeyboardKey.C);
            int id = isFirst ? 1 : 2;
            return new MacroDefinition(
                Id(id),
                $"Macro {id}",
                enabled: true,
                trigger,
                mode,
                sequence);
        }
    }

    private sealed class ControlledDelay : IDelay
    {
        private readonly TaskCompletionSource _release = NewSignal();

        internal TaskCompletionSource Entered { get; } = NewSignal();

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return _release.Task.WaitAsync(cancellationToken);
        }

        internal void Release() => _release.TrySetResult();
    }

    private class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private readonly object _gate = new();
        private readonly List<CountWaiter> _waiters = new();

        public virtual void KeyDown(KeyboardKey key) => Record($"Down:{key}");

        public virtual void KeyUp(KeyboardKey key) => Record($"Up:{key}");

        internal IReadOnlyList<string> Snapshot() => _calls.ToArray();

        internal Task WaitForCallCountAsync(int expected)
        {
            lock (_gate)
            {
                if (_calls.Count >= expected)
                {
                    return Task.CompletedTask;
                }

                var completion = NewSignal();
                _waiters.Add(new CountWaiter(expected, completion));
                return completion.Task;
            }
        }

        protected void Record(string value)
        {
            _calls.Enqueue(value);
            List<TaskCompletionSource>? ready = null;
            lock (_gate)
            {
                int count = _calls.Count;
                for (int index = _waiters.Count - 1; index >= 0; index--)
                {
                    CountWaiter waiter = _waiters[index];
                    if (count >= waiter.Expected)
                    {
                        ready ??= new List<TaskCompletionSource>();
                        ready.Add(waiter.Completion);
                        _waiters.RemoveAt(index);
                    }
                }
            }

            if (ready is not null)
            {
                foreach (TaskCompletionSource completion in ready)
                {
                    completion.TrySetResult();
                }
            }
        }

        private sealed record CountWaiter(
            int Expected,
            TaskCompletionSource Completion);
    }

    private sealed class FailFirstKeyboardOutput : RecordingKeyboardOutput
    {
        private readonly Exception _failure;
        private int _failed;

        internal FailFirstKeyboardOutput(Exception failure)
        {
            _failure = failure;
        }

        public override void KeyDown(KeyboardKey key)
        {
            Record($"Down:{key}");
            if (key == KeyboardKey.A &&
                Interlocked.Exchange(ref _failed, 1) == 0)
            {
                throw _failure;
            }
        }
    }

    private sealed class NoOpMouseOutput : IMouseOutput
    {
        public void ButtonDown(MouseButton button)
        {
        }

        public void ButtonUp(MouseButton button)
        {
        }
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        private readonly Exception _failure;

        internal ThrowingSynchronizationContext(Exception failure)
        {
            _failure = failure;
        }

        public override void Post(SendOrPostCallback callback, object? state) =>
            throw _failure;
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work =
            new();

        internal int QueuedCount
        {
            get
            {
                lock (_gate)
                {
                    return _work.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_gate)
            {
                _work.Enqueue((callback, state));
            }
        }

        internal void RunAll()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_gate)
                {
                    if (!_work.TryDequeue(out item))
                    {
                        return;
                    }
                }

                item.Callback(item.State);
            }
        }
    }
}
