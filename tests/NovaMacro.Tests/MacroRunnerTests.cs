using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class MacroRunnerTests
{
    [Fact]
    public void Constructor_RejectsNullKeyboardOutput()
    {
        Assert.Throws<ArgumentNullException>(
            () => new MacroRunner(null!, new RecordingDelay()));
    }

    [Fact]
    public void Constructor_RejectsNullDelay()
    {
        Assert.Throws<ArgumentNullException>(
            () => new MacroRunner(new RecordingKeyboardOutput(), null!));
    }

    [Fact]
    public async Task RunAsync_NullSequenceIsRejectedWithoutPoisoningRunner()
    {
        var keyboard = new RecordingKeyboardOutput();
        var runner = new MacroRunner(keyboard, new RecordingDelay());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runner.RunAsync(null!));

        MacroRunResult result = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.A)));

        Assert.Equal(MacroRunResult.Completed, result);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A)
            },
            keyboard.Events);
    }

    [Fact]
    public async Task RunAsync_AbcTestExecutesStepsInExactOrder()
    {
        var events = new List<RunnerEvent>();
        var runner = new MacroRunner(
            new RecordingKeyboardOutput(events),
            new RecordingDelay(events));

        MacroRunResult result = await runner.RunAsync(BuiltInMacroSequences.AbcTest);

        Assert.Equal(MacroRunResult.Completed, result);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A),
                new DelayEvent(TimeSpan.FromMilliseconds(150)),
                new KeyDownEvent(KeyboardKey.B),
                new KeyUpEvent(KeyboardKey.B),
                new DelayEvent(TimeSpan.FromMilliseconds(150)),
                new KeyDownEvent(KeyboardKey.C),
                new KeyUpEvent(KeyboardKey.C)
            },
            events);
    }

    [Fact]
    public async Task RunAsync_PassesExactDurationAndCancellationTokenToDelay()
    {
        var delay = new RecordingDelay();
        var runner = new MacroRunner(new RecordingKeyboardOutput(), delay);
        TimeSpan duration = TimeSpan.FromMilliseconds(275);
        using var cancellationSource = new CancellationTokenSource();

        MacroRunResult result = await runner.RunAsync(
            Sequence(new DelayStep(duration)),
            cancellationSource.Token);

        Assert.Equal(MacroRunResult.Completed, result);
        DelayInvocation invocation = Assert.Single(delay.Invocations);
        Assert.Equal(duration, invocation.Duration);
        Assert.Equal(cancellationSource.Token, invocation.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_EmptySequenceCompletesWithoutCallingDependencies()
    {
        var events = new List<RunnerEvent>();
        var keyboard = new RecordingKeyboardOutput(events);
        var delay = new RecordingDelay(events);
        var runner = new MacroRunner(keyboard, delay);

        MacroRunResult result = await runner.RunAsync(Sequence());

        Assert.Equal(MacroRunResult.Completed, result);
        Assert.Empty(events);
        Assert.Empty(keyboard.Events);
        Assert.Empty(delay.Invocations);
    }

    [Fact]
    public async Task RunAsync_UnsupportedStepStopsExecutionAndReleasesGate()
    {
        var keyboard = new RecordingKeyboardOutput();
        var delay = new RecordingDelay();
        var runner = new MacroRunner(keyboard, delay);
        var unsupportedSequence = Sequence(
            new UnsupportedStep(),
            new KeyTapStep(KeyboardKey.A));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => runner.RunAsync(unsupportedSequence));

        Assert.Empty(keyboard.Events);
        Assert.Empty(delay.Invocations);

        MacroRunResult laterResult = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.B)));

        Assert.Equal(MacroRunResult.Completed, laterResult);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.B),
                new KeyUpEvent(KeyboardKey.B)
            },
            keyboard.Events);
    }

    [Fact]
    public async Task RunAsync_ConcurrentCallReturnsAlreadyRunningWithoutQueuing()
    {
        var keyboard = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        var runner = new MacroRunner(keyboard, delay);
        Task<MacroRunResult> firstRun = runner.RunAsync(
            Sequence(new DelayStep(TimeSpan.FromMilliseconds(1))));
        await WaitForEntryAsync(delay);

        Task<MacroRunResult> secondRun = runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.A)));

        Assert.True(secondRun.IsCompletedSuccessfully);
        Assert.Equal(MacroRunResult.AlreadyRunning, await secondRun);
        Assert.Empty(keyboard.Events);
        Assert.Single(delay.Invocations);

        delay.Release();
        Assert.Equal(MacroRunResult.Completed, await firstRun);
        Assert.Empty(keyboard.Events);
    }

    [Fact]
    public async Task RunAsync_ReleasesGateAfterSuccessfulRun()
    {
        var keyboard = new RecordingKeyboardOutput();
        var runner = new MacroRunner(keyboard, new RecordingDelay());

        MacroRunResult firstResult = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.A)));
        MacroRunResult secondResult = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.B)));

        Assert.Equal(MacroRunResult.Completed, firstResult);
        Assert.Equal(MacroRunResult.Completed, secondResult);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A),
                new KeyDownEvent(KeyboardKey.B),
                new KeyUpEvent(KeyboardKey.B)
            },
            keyboard.Events);
    }

    [Fact]
    public async Task RunAsync_PreCancelledIdleRequestDoesNothingAndReleasesGate()
    {
        var keyboard = new RecordingKeyboardOutput();
        var delay = new RecordingDelay();
        var runner = new MacroRunner(keyboard, delay);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runner.RunAsync(
                    Sequence(
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1))),
                    cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Empty(keyboard.Events);
        Assert.Empty(delay.Invocations);

        MacroRunResult laterResult = await runner.RunAsync(Sequence());
        Assert.Equal(MacroRunResult.Completed, laterResult);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringDelayStopsRemainingStepsAndReleasesGate()
    {
        var keyboard = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        var runner = new MacroRunner(keyboard, delay);
        using var cancellationSource = new CancellationTokenSource();
        Task<MacroRunResult> run = runner.RunAsync(
            Sequence(
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                new KeyTapStep(KeyboardKey.A)),
            cancellationSource.Token);
        await WaitForEntryAsync(delay);

        cancellationSource.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Empty(keyboard.Events);
        Assert.Single(delay.Invocations);

        MacroRunResult laterResult = await runner.RunAsync(Sequence());
        Assert.Equal(MacroRunResult.Completed, laterResult);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringSuccessfulKeyDownStillReleasesKeyOnce()
    {
        using var cancellationSource = new CancellationTokenSource();
        var keyboard = new RecordingKeyboardOutput
        {
            KeyDownAction = _ => cancellationSource.Cancel()
        };
        var delay = new RecordingDelay();
        var runner = new MacroRunner(keyboard, delay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(
                Sequence(
                    new KeyTapStep(KeyboardKey.A),
                    new DelayStep(TimeSpan.FromMilliseconds(1)),
                    new KeyTapStep(KeyboardKey.B)),
                cancellationSource.Token));

        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A)
            },
            keyboard.Events);
        Assert.Empty(delay.Invocations);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringFinalSuccessfulKeyDownReleasesKeyAndCompletes()
    {
        using var cancellationSource = new CancellationTokenSource();
        var keyboard = new RecordingKeyboardOutput
        {
            KeyDownAction = _ => cancellationSource.Cancel()
        };
        var runner = new MacroRunner(keyboard, new RecordingDelay());

        MacroRunResult result = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.A)),
            cancellationSource.Token);

        Assert.Equal(MacroRunResult.Completed, result);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A)
            },
            keyboard.Events);
    }

    [Fact]
    public async Task RunAsync_KeyDownFailureSkipsKeyUpAndLaterStepsAndReleasesGate()
    {
        var expectedException = new InvalidOperationException("KeyDown failed.");
        var keyboard = new RecordingKeyboardOutput
        {
            KeyDownAction = _ => throw expectedException
        };
        var delay = new RecordingDelay();
        var runner = new MacroRunner(keyboard, delay);

        InvalidOperationException actualException =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.RunAsync(
                    Sequence(
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.B))));

        Assert.Same(expectedException, actualException);
        Assert.Equal(
            new RunnerEvent[] { new KeyDownEvent(KeyboardKey.A) },
            keyboard.Events);
        Assert.Empty(delay.Invocations);

        keyboard.KeyDownAction = null;
        MacroRunResult laterResult = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.C)));
        Assert.Equal(MacroRunResult.Completed, laterResult);
    }

    [Fact]
    public async Task RunAsync_KeyUpFailureStopsLaterStepsAndReleasesGate()
    {
        var expectedException = new InvalidOperationException("KeyUp failed.");
        var keyboard = new RecordingKeyboardOutput
        {
            KeyUpAction = _ => throw expectedException
        };
        var delay = new RecordingDelay();
        var runner = new MacroRunner(keyboard, delay);

        InvalidOperationException actualException =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.RunAsync(
                    Sequence(
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.B))));

        Assert.Same(expectedException, actualException);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A)
            },
            keyboard.Events);
        Assert.Empty(delay.Invocations);

        keyboard.KeyUpAction = null;
        MacroRunResult laterResult = await runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.C)));
        Assert.Equal(MacroRunResult.Completed, laterResult);
    }

    [Fact]
    public async Task RunAsync_BusyTakesPrecedenceOverCancelledToken()
    {
        var keyboard = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        var runner = new MacroRunner(keyboard, delay);
        Task<MacroRunResult> firstRun = runner.RunAsync(
            Sequence(new DelayStep(TimeSpan.FromMilliseconds(1))));
        await WaitForEntryAsync(delay);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Task<MacroRunResult> busyRun = runner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.A)),
            cancellationSource.Token);

        Assert.True(busyRun.IsCompletedSuccessfully);
        Assert.Equal(MacroRunResult.AlreadyRunning, await busyRun);
        Assert.Empty(keyboard.Events);

        delay.Release();
        Assert.Equal(MacroRunResult.Completed, await firstRun);
    }

    [Fact]
    public async Task RunAsync_DifferentRunnerInstancesHaveIndependentGates()
    {
        var firstDelay = new ControlledDelay();
        var firstRunner = new MacroRunner(
            new RecordingKeyboardOutput(),
            firstDelay);
        var secondKeyboard = new RecordingKeyboardOutput();
        var secondRunner = new MacroRunner(
            secondKeyboard,
            new RecordingDelay());
        Task<MacroRunResult> firstRun = firstRunner.RunAsync(
            Sequence(new DelayStep(TimeSpan.FromMilliseconds(1))));
        await WaitForEntryAsync(firstDelay);

        MacroRunResult secondResult = await secondRunner.RunAsync(
            Sequence(new KeyTapStep(KeyboardKey.A)));

        Assert.Equal(MacroRunResult.Completed, secondResult);
        Assert.Equal(
            new RunnerEvent[]
            {
                new KeyDownEvent(KeyboardKey.A),
                new KeyUpEvent(KeyboardKey.A)
            },
            secondKeyboard.Events);

        firstDelay.Release();
        Assert.Equal(MacroRunResult.Completed, await firstRun);
    }

    private static MacroSequence Sequence(params MacroStep[] steps)
    {
        return new MacroSequence(steps);
    }

    private static async Task WaitForEntryAsync(ControlledDelay delay)
    {
        await delay.Entered.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private abstract record RunnerEvent;

    private sealed record KeyDownEvent(KeyboardKey Key) : RunnerEvent;

    private sealed record KeyUpEvent(KeyboardKey Key) : RunnerEvent;

    private sealed record DelayEvent(TimeSpan Duration) : RunnerEvent;

    private sealed record DelayInvocation(
        TimeSpan Duration,
        CancellationToken CancellationToken);

    private sealed record UnsupportedStep : MacroStep;

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public RecordingKeyboardOutput(List<RunnerEvent>? events = null)
        {
            Events = events ?? new List<RunnerEvent>();
        }

        public List<RunnerEvent> Events { get; }

        public Action<KeyboardKey>? KeyDownAction { get; set; }

        public Action<KeyboardKey>? KeyUpAction { get; set; }

        public void KeyDown(KeyboardKey key)
        {
            Events.Add(new KeyDownEvent(key));
            KeyDownAction?.Invoke(key);
        }

        public void KeyUp(KeyboardKey key)
        {
            Events.Add(new KeyUpEvent(key));
            KeyUpAction?.Invoke(key);
        }
    }

    private sealed class RecordingDelay : IDelay
    {
        private readonly List<RunnerEvent>? _events;

        public RecordingDelay(List<RunnerEvent>? events = null)
        {
            _events = events;
        }

        public List<DelayInvocation> Invocations { get; } = new();

        public Func<TimeSpan, CancellationToken, Task>? WaitAsyncHandler { get; set; }

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(new DelayInvocation(duration, cancellationToken));
            _events?.Add(new DelayEvent(duration));

            return WaitAsyncHandler?.Invoke(duration, cancellationToken)
                ?? Task.CompletedTask;
        }
    }

    private sealed class ControlledDelay : IDelay
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public List<DelayInvocation> Invocations { get; } = new();

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(new DelayInvocation(duration, cancellationToken));
            _entered.TrySetResult();
            return _release.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }
}
