using System.Collections.Concurrent;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class HeldMouseAdversarialTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task RepeatPressBehindBusyOnceMustNotStartAfterTheOnceMacroFinishes()
    {
        var output = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = new MultiMacroHotkeyCoordinator(
            new ApplicationSettings(new[]
            {
                Macro(
                    1,
                    MouseButton.XButton2,
                    MacroExecutionMode.Once,
                    new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.B)
                    }),
                Macro(
                    2,
                    MouseButton.XButton1,
                    MacroExecutionMode.RepeatWhileHeld,
                    new MacroStep[] { new KeyTapStep(KeyboardKey.C) })
            }),
            () => throw new Xunit.Sdk.XunitException(
                "No keyboard backend is expected for mouse-only settings."),
            new MacroRunner(output, new NoOpMouseOutput(), delay),
            mouseButtonServiceFactory: () => service);
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 2);
        await scheduler.DrainAsync();
        await delay.Entered.Task.WaitAsync(SafetyTimeout);

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queuedContext);
            await scheduler.DrainAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(0, queuedContext.QueuedCount);

        delay.Release();
        await output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);

        queuedContext.RunAll();
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[] { "Down:A", "Up:A", "Down:B", "Up:B" },
            output.Snapshot());
    }

    [Fact]
    public async Task RejectedHeldPressStaysDroppedUntilReleaseThenNewPressCanRun()
    {
        var output = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = new MultiMacroHotkeyCoordinator(
            new ApplicationSettings(new[]
            {
                Macro(
                    1,
                    MouseButton.XButton2,
                    MacroExecutionMode.Once,
                    new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.B)
                    }),
                Macro(
                    2,
                    MouseButton.XButton1,
                    MacroExecutionMode.RepeatWhileHeld,
                    new MacroStep[] { new KeyTapStep(KeyboardKey.C) })
            }),
            () => throw new Xunit.Sdk.XunitException(
                "No keyboard backend is expected for mouse-only settings."),
            new MacroRunner(output, new NoOpMouseOutput(), delay),
            mouseButtonServiceFactory: () => service);
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 2);
        await scheduler.DrainAsync();
        await delay.Entered.Task.WaitAsync(SafetyTimeout);

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queuedContext);
            await scheduler.DrainAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(0, queuedContext.QueuedCount);

        delay.Release();
        await output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        Assert.Equal(
            new[] { "Down:A", "Up:A", "Down:B", "Up:B" },
            output.Snapshot());

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await scheduler.DrainAsync();
        await output.WaitForCallCountAsync(6).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[]
            {
                "Down:A", "Up:A", "Down:B", "Up:B",
                "Down:C", "Up:C"
            },
            output.Snapshot());
    }

    private static MacroDefinition Macro(
        int id,
        MouseButton button,
        MacroExecutionMode executionMode,
        IEnumerable<MacroStep> steps)
    {
        return new MacroDefinition(
            new MacroId(new Guid(id, 0, 0, new byte[8])),
            $"Macro {id}",
            enabled: true,
            new MouseButtonMacroTrigger(button),
            executionMode,
            new MacroSequence(steps));
    }

    private sealed class ControlledDelay : IDelay
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return _release.Task.WaitAsync(cancellationToken);
        }

        internal void Release() => _release.TrySetResult();
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private readonly List<CountWaiter> _waiters = new();
        private readonly object _gate = new();

        public void KeyDown(KeyboardKey key) => Record($"Down:{key}");

        public void KeyUp(KeyboardKey key) => Record($"Up:{key}");

        internal IReadOnlyList<string> Snapshot() => _calls.ToArray();

        internal Task WaitForCallCountAsync(int expected)
        {
            lock (_gate)
            {
                if (_calls.Count >= expected)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(new CountWaiter(expected, completion));
                return completion.Task;
            }
        }

        private void Record(string call)
        {
            _calls.Enqueue(call);
            List<TaskCompletionSource>? ready = null;
            lock (_gate)
            {
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

    private sealed class NoOpMouseOutput : IMouseOutput
    {
        public void ButtonDown(MouseButton button)
        {
        }

        public void ButtonUp(MouseButton button)
        {
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work =
            new();

        internal int QueuedCount => _work.Count;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _work.Enqueue((callback, state));
        }

        internal void RunAll()
        {
            while (_work.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}
