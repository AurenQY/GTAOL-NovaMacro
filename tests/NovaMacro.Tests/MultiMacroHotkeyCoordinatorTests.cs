using System.Collections.Concurrent;
using System.ComponentModel;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MultiMacroHotkeyCoordinatorTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public void Start_AllEnabledMacrosActivateInConfigurationOrder()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        ApplicationSettings settings = Settings(
            Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
            Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B),
            Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C));
        var factoryCalls = 0;
        using var coordinator = Coordinator(
            settings,
            () =>
            {
                factoryCalls++;
                return service;
            },
            new RecordingKeyboardOutput(),
            new ImmediateDelay());
        MultiMacroRuntimeStatus? published = null;
        coordinator.StatusChanged += status => published = status;

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(1, factoryCalls);
        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Same(status, coordinator.CurrentStatus);
        Assert.Same(status, published);
        Assert.Equal(
            new[] { 0x77u, 0x78u, 0x79u },
            native.RegisterCalls.Select(call => call.VirtualKey));
        Assert.Equal(
            new[] { Id(1), Id(2), Id(3) },
            status.Macros.Select(macro => macro.Id));
        Assert.All(
            status.Macros,
            macro => Assert.Equal(RuntimeMacroState.Active, macro.State));
    }

    [Fact]
    public void Start_DisabledMacroRemainsInOrderedStatusWithoutRegistration()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        ApplicationSettings settings = Settings(
            Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
            Macro(2, "B", false, HotkeyKey.F9, KeyboardKey.B),
            Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C));
        using var coordinator = Coordinator(
            settings,
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(
            new[] { 0x77u, 0x79u },
            native.RegisterCalls.Select(call => call.VirtualKey));
        Assert.Equal(
            new[]
            {
                RuntimeMacroState.Active,
                RuntimeMacroState.Disabled,
                RuntimeMacroState.Active
            },
            status.Macros.Select(macro => macro.State));
    }

    [Fact]
    public void Start_AllDisabledAndEmptySettingsDoNotCreateHotkeyService()
    {
        var allDisabledFactoryCalls = 0;
        var allDisabledKeyboard = new RecordingKeyboardOutput();
        using var allDisabled = Coordinator(
            Settings(
                Macro(1, "A", false, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", false, HotkeyKey.F9, KeyboardKey.B)),
            () =>
            {
                allDisabledFactoryCalls++;
                throw new Xunit.Sdk.XunitException("Factory must not be called.");
            },
            allDisabledKeyboard,
            new ImmediateDelay());

        MultiMacroRuntimeStatus disabledStatus = allDisabled.Start();

        Assert.Equal(0, allDisabledFactoryCalls);
        Assert.Equal(MultiMacroRuntimeState.NoEnabledMacros, disabledStatus.State);
        Assert.All(
            disabledStatus.Macros,
            macro => Assert.Equal(RuntimeMacroState.Disabled, macro.State));
        Assert.Equal(0, allDisabledKeyboard.CallCount);

        var emptyFactoryCalls = 0;
        var emptyKeyboard = new RecordingKeyboardOutput();
        using var empty = Coordinator(
            new ApplicationSettings(Array.Empty<MacroDefinition>()),
            () =>
            {
                emptyFactoryCalls++;
                throw new Xunit.Sdk.XunitException("Factory must not be called.");
            },
            emptyKeyboard,
            new ImmediateDelay());

        MultiMacroRuntimeStatus emptyStatus = empty.Start();

        Assert.Equal(0, emptyFactoryCalls);
        Assert.Equal(MultiMacroRuntimeState.NoEnabledMacros, emptyStatus.State);
        Assert.Empty(emptyStatus.Macros);
        Assert.Equal(0, emptyKeyboard.CallCount);
    }

    [Fact]
    public void Start_OrdinaryRegistrationFailureIsIsolatedAndLaterMacrosActivate()
    {
        const int expectedErrorCode = 1409;
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(2, expectedErrorCode);
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        MacroDefinition failedMacro = Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B);
        ApplicationSettings settings = Settings(
            Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
            failedMacro,
            Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C));
        using var coordinator = Coordinator(
            settings,
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.PartiallyActive, status.State);
        Assert.Same(status, coordinator.CurrentStatus);
        Assert.Equal(
            new[]
            {
                RuntimeMacroState.Active,
                RuntimeMacroState.RegistrationFailed,
                RuntimeMacroState.Active
            },
            status.Macros.Select(macro => macro.State));
        Assert.Contains("Windows rejected global hotkey", status.Macros[1].Diagnostic);
        Assert.True(failedMacro.Enabled);
        Assert.Equal(new[] { 1, 2, 3 }, native.RegisterCalls.Select(
            call => call.RegistrationId));
        Assert.Empty(native.UnregisterCalls);
    }

    [Fact]
    public async Task Start_WhenAllOrdinaryRegistrationsFailRemainsAvailableForCleanShutdown()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(1, 1409);
        native.FailRegisterCall(2, 1409);
        native.FailRegisterCall(3, 1409);
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        using var coordinator = Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B),
                Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C)),
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.NoRegistrationsActive, status.State);
        Assert.All(
            status.Macros,
            macro => Assert.Equal(RuntimeMacroState.RegistrationFailed, macro.State));
        Assert.Empty(native.UnregisterCalls);

        coordinator.Dispose();
        Assert.Null(await service.MessageLoopCompletion.WaitAsync(SafetyTimeout));
    }

    [Fact]
    public void Start_GlobalActivationFailureCleansAcquiredHandlesAndStopsLaterRegistration()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var activationFailure = new InvalidOperationException(
            "controlled global activation failure");
        native.ThrowOnRegisterCall(2, activationFailure);
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        ApplicationSettings settings = Settings(
            Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
            Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B),
            Macro(3, "C", false, HotkeyKey.F10, KeyboardKey.C));
        using var coordinator = Coordinator(
            settings,
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, status.State);
        Assert.Same(status, coordinator.CurrentStatus);
        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            new[]
            {
                RuntimeMacroState.RuntimeUnavailable,
                RuntimeMacroState.RuntimeUnavailable,
                RuntimeMacroState.Disabled
            },
            status.Macros.Select(macro => macro.State));
        Assert.All(
            status.Macros.Take(2),
            macro => Assert.Contains(activationFailure.Message, macro.Diagnostic));
    }

    [Fact]
    public async Task TriggeredHotkeys_RouteToTheirImmutableStartupSequences()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var keyboard = new RecordingKeyboardOutput();
        var source = new List<MacroDefinition>
        {
            Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
            Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B),
            Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C)
        };
        ApplicationSettings settings = new(source);
        using var coordinator = Coordinator(
            settings,
            () => service,
            keyboard,
            new ImmediateDelay());
        coordinator.Start();

        source.Clear();
        source.Add(Macro(99, "Different", true, HotkeyKey.F11, KeyboardKey.Enter));

        await TriggerAsync(native, scheduler, registrationId: 1);
        await keyboard.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        await TriggerAsync(native, scheduler, registrationId: 3);
        await keyboard.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        await TriggerAsync(native, scheduler, registrationId: 2);
        await keyboard.WaitForCallCountAsync(6).WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[]
            {
                new KeyboardCall("Down", KeyboardKey.A),
                new KeyboardCall("Up", KeyboardKey.A),
                new KeyboardCall("Down", KeyboardKey.C),
                new KeyboardCall("Up", KeyboardKey.C),
                new KeyboardCall("Down", KeyboardKey.B),
                new KeyboardCall("Up", KeyboardKey.B)
            },
            keyboard.Snapshot());
        Assert.Equal(
            new[] { Id(1), Id(2), Id(3) },
            coordinator.CurrentStatus!.Macros.Select(macro => macro.Id));
    }

    [Fact]
    public async Task SharedRunner_DropsConcurrentTriggerAndAllowsLaterSequentialExecution()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var keyboard = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        using var coordinator = Coordinator(
            Settings(
                Macro(
                    1,
                    "A",
                    true,
                    HotkeyKey.F8,
                    new MacroSequence(
                        new MacroStep[]
                        {
                            new KeyTapStep(KeyboardKey.A),
                            new DelayStep(TimeSpan.FromMilliseconds(1)),
                            new KeyTapStep(KeyboardKey.C)
                        })),
                Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B)),
            () => service,
            keyboard,
            delay);
        var executionFailures = new ConcurrentQueue<(MacroId Id, Exception Error)>();
        coordinator.ExecutionFailed += (id, exception) =>
            executionFailures.Enqueue((id, exception));
        coordinator.Start();

        await TriggerThroughQueuedContextAsync(native, scheduler, 1);
        await delay.Entered.Task.WaitAsync(SafetyTimeout);
        Assert.Equal(
            new[]
            {
                new KeyboardCall("Down", KeyboardKey.A),
                new KeyboardCall("Up", KeyboardKey.A)
            },
            keyboard.Snapshot());

        await TriggerExpectingNoCoordinatorWorkerAsync(native, scheduler, 2);
        Assert.Equal(2, keyboard.CallCount);
        Assert.Empty(executionFailures);

        delay.Release();
        await keyboard.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        await TriggerThroughQueuedContextAsync(native, scheduler, 2);
        await keyboard.WaitForCallCountAsync(6).WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[]
            {
                new KeyboardCall("Down", KeyboardKey.A),
                new KeyboardCall("Up", KeyboardKey.A),
                new KeyboardCall("Down", KeyboardKey.C),
                new KeyboardCall("Up", KeyboardKey.C),
                new KeyboardCall("Down", KeyboardKey.B),
                new KeyboardCall("Up", KeyboardKey.B)
            },
            keyboard.Snapshot());
        Assert.Empty(executionFailures);
        Assert.Equal(MultiMacroRuntimeState.Active, coordinator.CurrentStatus?.State);
    }

    [Fact]
    public async Task ExecutionException_ReportsBoundMacroAndDoesNotChangeRuntimeStatus()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var executionFailure = new InvalidOperationException("controlled execution failure");
        var keyboard = new RecordingKeyboardOutput(
            keyDownFailure: (KeyboardKey.A, executionFailure));
        using var coordinator = Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B)),
            () => service,
            keyboard,
            new ImmediateDelay());
        var observed = NewSignal<(MacroId Id, Exception Error)>();
        coordinator.ExecutionFailed += (_, _) =>
            throw new InvalidOperationException("subscriber failure");
        coordinator.ExecutionFailed += (id, exception) => observed.TrySetResult((id, exception));
        coordinator.Start();

        await TriggerAsync(native, scheduler, registrationId: 1);
        (MacroId Id, Exception Error) failure = await observed.Task.WaitAsync(
            SafetyTimeout);

        Assert.Equal(Id(1), failure.Id);
        Assert.Same(executionFailure, failure.Error);
        Assert.Equal(MultiMacroRuntimeState.Active, coordinator.CurrentStatus?.State);
        Assert.All(
            coordinator.CurrentStatus!.Macros,
            status => Assert.Equal(RuntimeMacroState.Active, status.State));

        await TriggerAsync(native, scheduler, registrationId: 2);
        await keyboard.WaitForCallCountAsync(3).WaitAsync(SafetyTimeout);
        Assert.Equal(
            new[]
            {
                new KeyboardCall("Down", KeyboardKey.A),
                new KeyboardCall("Down", KeyboardKey.B),
                new KeyboardCall("Up", KeyboardKey.B)
            },
            keyboard.Snapshot());
    }

    [Fact]
    public async Task TerminalFailureAfterStart_ReplacesEveryEnabledStatusAndBlocksFutureTriggers()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var keyboard = new RecordingKeyboardOutput();
        var coordinator = Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", false, HotkeyKey.F9, KeyboardKey.B),
                Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C)),
            () => service,
            keyboard,
            new ImmediateDelay());
        MultiMacroRuntimeStatus initial = coordinator.Start();
        var terminalStatus = NewSignal<MultiMacroRuntimeStatus>();
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.ServiceUnavailable)
            {
                terminalStatus.TrySetResult(status);
            }
        };

        native.FailNextGetMessage();
        MultiMacroRuntimeStatus terminal = await terminalStatus.Task.WaitAsync(
            SafetyTimeout);

        Assert.NotSame(initial, terminal);
        Assert.Same(terminal, coordinator.CurrentStatus);
        Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, terminal.State);
        Assert.Equal(
            new[]
            {
                RuntimeMacroState.RuntimeUnavailable,
                RuntimeMacroState.Disabled,
                RuntimeMacroState.RuntimeUnavailable
            },
            terminal.Macros.Select(macro => macro.State));

        native.EnqueueHotkey(registrationId: 1);
        Assert.Equal(0, scheduler.QueuedCount);
        Assert.Equal(0, keyboard.CallCount);
        Assert.IsType<Win32Exception>(CaptureException(coordinator.Dispose));
    }

    [Fact]
    public void Dispose_AttemptsEveryHandleThenServiceCleanupWhenOneHandleFails()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", true, HotkeyKey.F9, KeyboardKey.B),
                Macro(3, "C", true, HotkeyKey.F10, KeyboardKey.C)),
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());
        coordinator.Start();
        native.FailNextUnregister();

        Exception failure = CaptureException(coordinator.Dispose)
            ?? throw new Xunit.Sdk.XunitException("Expected cleanup failure.");

        Assert.IsType<Win32Exception>(failure);
        Assert.Equal(new[] { 1, 2, 3, 1 }, native.UnregisterCalls);
        coordinator.Dispose();
    }

    [Fact]
    public void StatusSnapshot_IsImmutableAndCurrentStatusTracksLifecycle()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", false, HotkeyKey.F9, KeyboardKey.B)),
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());

        Assert.Null(coordinator.CurrentStatus);
        MultiMacroRuntimeStatus started = coordinator.Start();
        IReadOnlyList<RuntimeMacroStatus> macros = started.Macros;
        IList<RuntimeMacroStatus> mutableView = Assert.IsAssignableFrom<
            IList<RuntimeMacroStatus>>(macros);

        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.RemoveAt(0));
        Assert.Same(started, coordinator.CurrentStatus);
        Assert.Equal(new[] { Id(1), Id(2) }, macros.Select(macro => macro.Id));

        coordinator.Dispose();
        Assert.Same(started, coordinator.CurrentStatus);
    }

    [Fact]
    public void Constructor_DefensivelyRejectsDuplicateIdsAndEnabledHotkeys()
    {
        var factoryCalls = 0;
        Func<WindowsGlobalHotkeyService> factory = () =>
        {
            factoryCalls++;
            throw new Xunit.Sdk.XunitException("Factory must not be called.");
        };

        Assert.Throws<InvalidOperationException>(() => Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(1, "B", true, HotkeyKey.F9, KeyboardKey.B)),
            factory,
            new RecordingKeyboardOutput(),
            new ImmediateDelay()));
        Assert.Throws<InvalidOperationException>(() => Coordinator(
            Settings(
                Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A),
                Macro(2, "B", true, HotkeyKey.F8, KeyboardKey.B)),
            factory,
            new RecordingKeyboardOutput(),
            new ImmediateDelay()));
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void Start_CanOnlyRunOnceAndCannotRunAfterDispose()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var coordinator = Coordinator(
            Settings(Macro(1, "A", true, HotkeyKey.F8, KeyboardKey.A)),
            () => service,
            new RecordingKeyboardOutput(),
            new ImmediateDelay());

        coordinator.Start();
        Assert.Throws<InvalidOperationException>(coordinator.Start);
        Assert.Single(native.RegisterCalls);

        coordinator.Dispose();
        Assert.Throws<InvalidOperationException>(coordinator.Start);
        coordinator.Dispose();
    }

    private static MultiMacroHotkeyCoordinator Coordinator(
        ApplicationSettings settings,
        Func<WindowsGlobalHotkeyService> factory,
        IKeyboardOutput keyboard,
        IDelay delay)
    {
        return new MultiMacroHotkeyCoordinator(
            settings,
            factory,
            new MacroRunner(keyboard, delay));
    }

    private static ApplicationSettings Settings(params MacroDefinition[] macros)
    {
        return new ApplicationSettings(macros);
    }

    private static MacroDefinition Macro(
        int id,
        string name,
        bool enabled,
        HotkeyKey hotkey,
        KeyboardKey output)
    {
        return Macro(
            id,
            name,
            enabled,
            hotkey,
            new MacroSequence(
                new MacroStep[] { new KeyTapStep(output) }));
    }

    private static MacroDefinition Macro(
        int id,
        string name,
        bool enabled,
        HotkeyKey hotkey,
        MacroSequence sequence)
    {
        return new MacroDefinition(
            Id(id),
            name,
            enabled,
            new KeyboardHotkey(hotkey),
            sequence);
    }

    private static MacroId Id(int value)
    {
        return new MacroId(new Guid(value, 0, 0, new byte[8]));
    }

    private static async Task TriggerAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler,
        int registrationId)
    {
        native.EnqueueHotkey(registrationId);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await scheduler.RunNextAsync().WaitAsync(SafetyTimeout);
    }

    private static async Task TriggerThroughQueuedContextAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler,
        int registrationId)
    {
        native.EnqueueHotkey(registrationId);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        var context = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            Task worker = scheduler.RunNextAsync();
            Assert.True(worker.IsCompleted);
            await worker;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(1, context.QueuedCount);
        context.RunAll();
    }

    private static async Task TriggerExpectingNoCoordinatorWorkerAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler,
        int registrationId)
    {
        native.EnqueueHotkey(registrationId);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        var context = new QueuedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            Task worker = scheduler.RunNextAsync();
            Assert.True(worker.IsCompleted);
            await worker;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(0, context.QueuedCount);
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
        private readonly ConcurrentQueue<KeyboardCall> _calls = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _waiters = new();
        private readonly (KeyboardKey Key, Exception Exception)? _keyDownFailure;

        internal RecordingKeyboardOutput(
            (KeyboardKey Key, Exception Exception)? keyDownFailure = null)
        {
            _keyDownFailure = keyDownFailure;
        }

        internal int CallCount => _calls.Count;

        public void KeyDown(KeyboardKey key)
        {
            Add("Down", key);

            if (_keyDownFailure is { } failure && failure.Key == key)
            {
                throw failure.Exception;
            }
        }

        public void KeyUp(KeyboardKey key)
        {
            Add("Up", key);
        }

        internal IReadOnlyList<KeyboardCall> Snapshot()
        {
            return _calls.ToArray();
        }

        internal Task WaitForCallCountAsync(int expectedCount)
        {
            if (CallCount >= expectedCount)
            {
                return Task.CompletedTask;
            }

            return _waiters.GetOrAdd(
                expectedCount,
                _ => new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        private void Add(string operation, KeyboardKey key)
        {
            _calls.Enqueue(new KeyboardCall(operation, key));
            int count = CallCount;

            foreach ((int expectedCount, TaskCompletionSource waiter) in _waiters)
            {
                if (count >= expectedCount)
                {
                    waiter.TrySetResult();
                }
            }
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

        internal TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return _release.Task.WaitAsync(cancellationToken);
        }

        internal void Release()
        {
            _release.TrySetResult();
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
            while (_callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
            }
        }
    }

    private sealed record KeyboardCall(string Operation, KeyboardKey Key);
}
