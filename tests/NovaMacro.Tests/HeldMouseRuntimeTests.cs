using System.Collections.Concurrent;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class HeldMouseRuntimeTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task QuickDownUpBeforeWorkerStartsExecutesOneCompleteIteration()
    {
        var log = new ConcurrentQueue<string>();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = MouseCoordinator(
            service,
            RepeatMacro(MouseButton.XButton1, RapidFireSequence()),
            log,
            new ImmediateDelay());
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        QueuedSynchronizationContext context =
            await DrainNotificationsHoldingWorkersAsync(scheduler);

        Assert.Equal(1, context.QueuedCount);
        Assert.Empty(log);
        context.RunAll();
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(RapidFireIteration(), log);
    }

    [Fact]
    public async Task HeldButtonRepeatsFullIterationsSeriallyAndReleaseStopsNext()
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = MouseCoordinator(
            service,
            RepeatMacro(MouseButton.XButton1, RapidFireSequence()),
            log,
            delay);
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        for (int call = 1; call <= 3; call++)
        {
            await delay.WaitForCallCountAsync(call).WaitAsync(SafetyTimeout);
            delay.Release(call - 1);
        }
        await delay.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await scheduler.DrainAsync();
        for (int call = 4; call <= 6; call++)
        {
            await delay.WaitForCallCountAsync(call).WaitAsync(SafetyTimeout);
            delay.Release(call - 1);
        }
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(RapidFireIteration().Concat(RapidFireIteration()), log);
        Assert.Equal(6, delay.CallCount);
    }

    [Fact]
    public async Task ReleaseAfterDigit5StillCompletesDigit4BeforeStopping()
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = MouseCoordinator(
            service,
            RepeatMacro(MouseButton.XButton1, RapidFireSequence()),
            log,
            delay);
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        await delay.WaitForCallCountAsync(1).WaitAsync(SafetyTimeout);
        delay.Release(0);
        await delay.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);
        Assert.Contains("KeyDown:Digit5", log);

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await scheduler.DrainAsync();
        delay.Release(1);
        await delay.WaitForCallCountAsync(3).WaitAsync(SafetyTimeout);
        delay.Release(2);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(RapidFireIteration(), log);
    }

    [Fact]
    public async Task DuplicateDownDoesNotStartSecondSessionAndNewPressAfterUpDoes()
    {
        var log = new ConcurrentQueue<string>();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = MouseCoordinator(
            service,
            OnceMacro(MouseButton.XButton1, KeySequence(KeyboardKey.A)),
            log,
            new ImmediateDelay());
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[] { "KeyDown:A", "KeyUp:A", "KeyDown:A", "KeyUp:A" },
            log);
    }

    [Fact]
    public async Task HeldSessionContinuouslyOwnsGlobalSlotAndDropsKeyboardAndMouse5()
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var keyboardNative = new FakeGlobalHotkeyNativeApi();
        var keyboardScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var keyboardService = new WindowsGlobalHotkeyService(
            keyboardNative, keyboardScheduler);
        using var mouseNative = new FakeGlobalMouseButtonNativeApi();
        var mouseScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var mouseService = new WindowsGlobalMouseButtonService(
            mouseNative, mouseScheduler);
        ApplicationSettings settings = Settings(
            RepeatMacro(MouseButton.XButton1, new MacroSequence(new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.A),
                new DelayStep(TimeSpan.FromMilliseconds(1)),
                new KeyTapStep(KeyboardKey.C)
            })),
            OnceKeyboardMacro(2, HotkeyKey.F8, KeyboardKey.B),
            OnceMacro(MouseButton.XButton2, KeySequence(KeyboardKey.D), id: 3));
        using var coordinator = Coordinator(
            settings,
            () => keyboardService,
            () => mouseService,
            log,
            delay);
        coordinator.Start();

        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await mouseScheduler.DrainAsync();
        await delay.WaitForCallCountAsync(1).WaitAsync(SafetyTimeout);

        keyboardNative.EnqueueHotkey(1);
        await keyboardScheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await keyboardScheduler.RunNextAsync();
        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 2);
        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 2);
        await mouseScheduler.DrainAsync();
        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await mouseScheduler.DrainAsync();
        delay.Release(0);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[] { "KeyDown:A", "KeyUp:A", "KeyDown:C", "KeyUp:C" },
            log);
    }

    [Fact]
    public async Task RepeatPressWhileOneShotRunnerBusyIsDroppedWithoutRetry()
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var keyboardNative = new FakeGlobalHotkeyNativeApi();
        var keyboardScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var keyboardService = new WindowsGlobalHotkeyService(
            keyboardNative, keyboardScheduler);
        using var mouseNative = new FakeGlobalMouseButtonNativeApi();
        var mouseScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var mouseService = new WindowsGlobalMouseButtonService(
            mouseNative, mouseScheduler);
        ApplicationSettings settings = Settings(
            new MacroDefinition(
                Id(1),
                "Busy",
                true,
                new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F8)),
                MacroExecutionMode.Once,
                new MacroSequence(new MacroStep[]
                {
                    new KeyTapStep(KeyboardKey.A),
                    new DelayStep(TimeSpan.FromMilliseconds(1)),
                    new KeyTapStep(KeyboardKey.B)
                })),
            RepeatMacro(MouseButton.XButton1, KeySequence(KeyboardKey.C), id: 2));
        using var coordinator = Coordinator(
            settings,
            () => keyboardService,
            () => mouseService,
            log,
            delay);
        coordinator.Start();

        keyboardNative.EnqueueHotkey(1);
        await keyboardScheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await keyboardScheduler.RunNextAsync();
        await delay.WaitForCallCountAsync(1).WaitAsync(SafetyTimeout);
        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await mouseScheduler.DrainAsync();
        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await mouseScheduler.DrainAsync();
        delay.Release(0);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[] { "KeyDown:A", "KeyUp:A", "KeyDown:B", "KeyUp:B" },
            log);
    }

    [Fact]
    public async Task ExecutionFailureIsReportedOnceAndTerminatesHeldSession()
    {
        var expected = new InvalidOperationException("controlled failure");
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        var keyboard = new FailingKeyboardOutput(expected);
        var runner = new MacroRunner(keyboard, new RecordingMouseOutput(
            new ConcurrentQueue<string>()), new ImmediateDelay());
        using var coordinator = new MultiMacroHotkeyCoordinator(
            Settings(RepeatMacro(MouseButton.XButton1, KeySequence(KeyboardKey.A))),
            () => throw new Xunit.Sdk.XunitException("No keyboard backend."),
            runner,
            mouseButtonServiceFactory: () => service);
        var failure = new TaskCompletionSource<(MacroId Id, Exception Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.ExecutionFailed += (id, error) => failure.TrySetResult((id, error));
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        (MacroId Id, Exception Error) observed = await failure.Task.WaitAsync(
            SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(Id(1), observed.Id);
        Assert.Same(expected, observed.Error);
        Assert.Equal(1, keyboard.CallCount);
    }

    [Fact]
    public async Task DisposeHardCancelsHeldIterationAndLateReleaseCannotRestart()
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        var coordinator = MouseCoordinator(
            service,
            RepeatMacro(MouseButton.XButton1, new MacroSequence(new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.A),
                new DelayStep(TimeSpan.FromMilliseconds(1)),
                new KeyTapStep(KeyboardKey.B)
            })),
            log,
            delay);
        coordinator.Start();
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        await delay.WaitForCallCountAsync(1).WaitAsync(SafetyTimeout);

        coordinator.Dispose();
        nint lateUp = native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await scheduler.DrainAsync();

        Assert.Equal(new[] { "KeyDown:A", "KeyUp:A" }, log);
        Assert.Equal((nint)5678, lateUp);
    }

    [Fact]
    public void MouseBackendFailureLeavesKeyboardMacroPartiallyActive()
    {
        using var keyboardNative = new FakeGlobalHotkeyNativeApi();
        using var keyboardService = new WindowsGlobalHotkeyService(keyboardNative);
        using var mouseNative = new FakeGlobalMouseButtonNativeApi();
        mouseNative.FailNextInstall(1428);
        using var mouseService = new WindowsGlobalMouseButtonService(mouseNative);
        var log = new ConcurrentQueue<string>();
        using var coordinator = Coordinator(
            Settings(
                OnceKeyboardMacro(1, HotkeyKey.F8, KeyboardKey.A),
                OnceMacro(MouseButton.XButton1, KeySequence(KeyboardKey.B), id: 2)),
            () => keyboardService,
            () => mouseService,
            log,
            new ImmediateDelay());

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.PartiallyActive, status.State);
        Assert.Equal(RuntimeMacroState.Active, status.Macros[0].State);
        Assert.Equal(RuntimeMacroState.RegistrationFailed, status.Macros[1].State);
    }

    [Fact]
    public async Task MouseTerminalFailureHardCancelsHeldSessionButKeyboardRemainsUsable()
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var keyboardNative = new FakeGlobalHotkeyNativeApi();
        var keyboardScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var keyboardService = new WindowsGlobalHotkeyService(
            keyboardNative, keyboardScheduler);
        using var mouseNative = new FakeGlobalMouseButtonNativeApi();
        var mouseScheduler = new FakeHotkeyTriggerWorkScheduler();
        var mouseService = new WindowsGlobalMouseButtonService(
            mouseNative, mouseScheduler);
        using var coordinator = Coordinator(
            Settings(
                OnceKeyboardMacro(1, HotkeyKey.F8, KeyboardKey.B),
                RepeatMacro(
                    MouseButton.XButton1,
                    new MacroSequence(new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.C)
                    }),
                    id: 2)),
            () => keyboardService,
            () => mouseService,
            log,
            delay);
        var terminal = new TaskCompletionSource<MultiMacroRuntimeStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.PartiallyActive &&
                status.Macros[1].State == RuntimeMacroState.RuntimeUnavailable)
            {
                terminal.TrySetResult(status);
            }
        };
        coordinator.Start();
        mouseNative.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await mouseScheduler.DrainAsync();
        await delay.WaitForCallCountAsync(1).WaitAsync(SafetyTimeout);

        mouseNative.FailNextGetMessage();
        MultiMacroRuntimeStatus status = await terminal.Task.WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        keyboardNative.EnqueueHotkey(1);
        await keyboardScheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await keyboardScheduler.RunNextAsync();
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(RuntimeMacroState.Active, status.Macros[0].State);
        Assert.Equal(RuntimeMacroState.RuntimeUnavailable, status.Macros[1].State);
        Assert.Equal(
            new[]
            {
                "KeyDown:A", "KeyUp:A",
                "KeyDown:B", "KeyUp:B"
            },
            log);
        Assert.ThrowsAny<Exception>(mouseService.Dispose);
    }

    private static MultiMacroHotkeyCoordinator MouseCoordinator(
        WindowsGlobalMouseButtonService service,
        MacroDefinition macro,
        ConcurrentQueue<string> log,
        IDelay delay) => Coordinator(
            Settings(macro),
            () => throw new Xunit.Sdk.XunitException("No keyboard backend."),
            () => service,
            log,
            delay);

    private static MultiMacroHotkeyCoordinator Coordinator(
        ApplicationSettings settings,
        Func<WindowsGlobalHotkeyService> keyboardFactory,
        Func<WindowsGlobalMouseButtonService> mouseFactory,
        ConcurrentQueue<string> log,
        IDelay delay) => new(
            settings,
            keyboardFactory,
            new MacroRunner(
                new RecordingKeyboardOutput(log),
                new RecordingMouseOutput(log),
                delay),
            mouseButtonServiceFactory: mouseFactory);

    private static ApplicationSettings Settings(params MacroDefinition[] macros) =>
        new(macros);

    private static MacroDefinition RepeatMacro(
        MouseButton button,
        MacroSequence sequence,
        int id = 1) => new(
            Id(id),
            $"Repeat {id}",
            true,
            new MouseButtonMacroTrigger(button),
            MacroExecutionMode.RepeatWhileHeld,
            sequence);

    private static MacroDefinition OnceMacro(
        MouseButton button,
        MacroSequence sequence,
        int id = 1) => new(
            Id(id),
            $"Once {id}",
            true,
            new MouseButtonMacroTrigger(button),
            MacroExecutionMode.Once,
            sequence);

    private static MacroDefinition OnceKeyboardMacro(
        int id,
        HotkeyKey hotkey,
        KeyboardKey output) => new(
            Id(id),
            $"Keyboard {id}",
            true,
            new KeyboardMacroTrigger(new KeyboardHotkey(hotkey)),
            MacroExecutionMode.Once,
            KeySequence(output));

    private static MacroSequence RapidFireSequence() => new(new MacroStep[]
    {
        new MouseButtonTapStep(MouseButton.Left),
        new DelayStep(TimeSpan.FromMilliseconds(1)),
        new KeyTapStep(KeyboardKey.Digit5),
        new DelayStep(TimeSpan.FromMilliseconds(1)),
        new KeyTapStep(KeyboardKey.Digit4),
        new DelayStep(TimeSpan.FromMilliseconds(1))
    });

    private static string[] RapidFireIteration() => new[]
    {
        "MouseDown:Left", "MouseUp:Left",
        "KeyDown:Digit5", "KeyUp:Digit5",
        "KeyDown:Digit4", "KeyUp:Digit4"
    };

    private static MacroSequence KeySequence(KeyboardKey key) =>
        new(new MacroStep[] { new KeyTapStep(key) });

    private static MacroId Id(int value) =>
        new(new Guid(value, 0, 0, new byte[8]));

    private static async Task<QueuedSynchronizationContext>
        DrainNotificationsHoldingWorkersAsync(
            FakeHotkeyTriggerWorkScheduler scheduler)
    {
        var context = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            Task drain = scheduler.DrainAsync();
            Assert.True(drain.IsCompleted);
            await drain;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        return context;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly ConcurrentQueue<string> _log;
        internal RecordingKeyboardOutput(ConcurrentQueue<string> log) => _log = log;
        public void KeyDown(KeyboardKey key) => _log.Enqueue($"KeyDown:{key}");
        public void KeyUp(KeyboardKey key) => _log.Enqueue($"KeyUp:{key}");
    }

    private sealed class RecordingMouseOutput : IMouseOutput
    {
        private readonly ConcurrentQueue<string> _log;
        internal RecordingMouseOutput(ConcurrentQueue<string> log) => _log = log;
        public void ButtonDown(MouseButton button) =>
            _log.Enqueue($"MouseDown:{button}");
        public void ButtonUp(MouseButton button) =>
            _log.Enqueue($"MouseUp:{button}");
    }

    private sealed class FailingKeyboardOutput : IKeyboardOutput
    {
        private readonly Exception _failure;
        internal FailingKeyboardOutput(Exception failure) => _failure = failure;
        internal int CallCount { get; private set; }
        public void KeyDown(KeyboardKey key)
        {
            CallCount++;
            throw _failure;
        }
        public void KeyUp(KeyboardKey key) =>
            throw new Xunit.Sdk.XunitException("KeyUp must not run.");
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SequencedDelay : IDelay
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _calls = new();
        private readonly List<CountWaiter> _waiters = new();

        internal int CallCount
        {
            get { lock (_gate) return _calls.Count; }
        }

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            List<TaskCompletionSource>? ready = null;
            lock (_gate)
            {
                _calls.Add(completion);
                int count = _calls.Count;
                for (int index = _waiters.Count - 1; index >= 0; index--)
                {
                    if (count >= _waiters[index].Expected)
                    {
                        ready ??= new List<TaskCompletionSource>();
                        ready.Add(_waiters[index].Completion);
                        _waiters.RemoveAt(index);
                    }
                }
            }
            if (ready is not null)
            {
                foreach (TaskCompletionSource waiter in ready) waiter.TrySetResult();
            }
            return completion.Task.WaitAsync(cancellationToken);
        }

        internal Task WaitForCallCountAsync(int expected)
        {
            lock (_gate)
            {
                if (_calls.Count >= expected) return Task.CompletedTask;
                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(new CountWaiter(expected, completion));
                return completion.Task;
            }
        }

        internal void Release(int index)
        {
            TaskCompletionSource completion;
            lock (_gate) completion = _calls[index];
            completion.TrySetResult();
        }

        private sealed record CountWaiter(
            int Expected,
            TaskCompletionSource Completion);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work =
            new();
        internal int QueuedCount => _work.Count;
        public override void Post(SendOrPostCallback callback, object? state) =>
            _work.Enqueue((callback, state));
        internal void RunAll()
        {
            while (_work.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}
