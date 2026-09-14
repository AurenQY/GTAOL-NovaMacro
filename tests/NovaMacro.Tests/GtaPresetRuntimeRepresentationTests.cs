using System.Collections.Concurrent;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.App.Presets;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class GtaPresetRuntimeRepresentationTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task RapidFirePreset_UsesSealedQuickHoldAndReleaseRuntimeBehavior()
    {
        BuiltInPresetDefinition preset = Assert.Single(
            BuiltInPresetCatalog.All,
            candidate => candidate.Id ==
                BuiltInPresetCatalog.HeavyWeaponRapidFirePresetId);

        IReadOnlyList<string> quickTap = await RunQuickTapAsync(preset);
        IReadOnlyList<string> holdAndRelease = await RunHoldAndReleaseAsync(preset);

        Assert.Equal(Iteration(), quickTap);
        Assert.Equal(Iteration().Concat(Iteration()), holdAndRelease);
    }

    private static async Task<IReadOnlyList<string>> RunQuickTapAsync(
        BuiltInPresetDefinition preset)
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = CreateCoordinator(service, preset, log, delay);
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await scheduler.DrainAsync();
        await ReleaseDelaysAsync(delay, 1, 3);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        return log.ToArray();
    }

    private static async Task<IReadOnlyList<string>> RunHoldAndReleaseAsync(
        BuiltInPresetDefinition preset)
    {
        var log = new ConcurrentQueue<string>();
        var delay = new SequencedDelay();
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using var coordinator = CreateCoordinator(service, preset, log, delay);
        coordinator.Start();

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.DrainAsync();
        await ReleaseDelaysAsync(delay, 1, 3);
        await delay.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1);
        await scheduler.DrainAsync();
        await ReleaseDelaysAsync(delay, 4, 6);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        return log.ToArray();
    }

    private static async Task ReleaseDelaysAsync(
        SequencedDelay delay,
        int firstCall,
        int lastCall)
    {
        for (int call = firstCall; call <= lastCall; call++)
        {
            await delay.WaitForCallCountAsync(call).WaitAsync(SafetyTimeout);
            delay.Release(call - 1);
        }
    }

    private static MultiMacroHotkeyCoordinator CreateCoordinator(
        WindowsGlobalMouseButtonService service,
        BuiltInPresetDefinition preset,
        ConcurrentQueue<string> log,
        IDelay delay)
    {
        var settings = new ApplicationSettings(new[]
        {
            new MacroDefinition(
                new MacroId(Guid.NewGuid()),
                preset.Name,
                preset.Enabled,
                preset.Trigger,
                preset.ExecutionMode,
                preset.Sequence)
        });

        return new MultiMacroHotkeyCoordinator(
            settings,
            () => throw new Xunit.Sdk.XunitException(
                "Rapid Fire is a mouse-only preset."),
            new MacroRunner(
                new RecordingKeyboardOutput(log),
                new RecordingMouseOutput(log),
                delay),
            mouseButtonServiceFactory: () => service);
    }

    private static string[] Iteration() => new[]
    {
        "MouseDown:Left", "MouseUp:Left",
        "KeyDown:Digit5", "KeyUp:Digit5",
        "KeyDown:Digit4", "KeyUp:Digit4"
    };

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
        public void ButtonDown(MouseButton button) => _log.Enqueue($"MouseDown:{button}");
        public void ButtonUp(MouseButton button) => _log.Enqueue($"MouseUp:{button}");
    }

    private sealed class SequencedDelay : IDelay
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _calls = new();
        private readonly List<CountWaiter> _waiters = new();

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
                for (int index = _waiters.Count - 1; index >= 0; index--)
                {
                    if (_calls.Count >= _waiters[index].Expected)
                    {
                        ready ??= new List<TaskCompletionSource>();
                        ready.Add(_waiters[index].Completion);
                        _waiters.RemoveAt(index);
                    }
                }
            }

            if (ready is not null)
            {
                foreach (TaskCompletionSource waiter in ready)
                {
                    waiter.TrySetResult();
                }
            }

            return completion.Task.WaitAsync(cancellationToken);
        }

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

        internal void Release(int index)
        {
            TaskCompletionSource completion;
            lock (_gate)
            {
                completion = _calls[index];
            }

            completion.TrySetResult();
        }

        private sealed record CountWaiter(
            int Expected,
            TaskCompletionSource Completion);
    }
}
