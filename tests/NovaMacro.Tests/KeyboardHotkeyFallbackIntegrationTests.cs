using System.Collections.Concurrent;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class KeyboardHotkeyFallbackIntegrationTests
{
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public void UncontestedKeyboardHotkeyUsesNativeServiceWithoutCreatingFallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var fallbackFactoryCalls = 0;
        using var coordinator = Coordinator(
            Settings(KeyboardMacro(
                1,
                new KeyboardHotkey(HotkeyKey.F8),
                KeyboardKey.A)),
            service,
            new RecordingKeyboardOutput(),
            fallbackFactory: () =>
            {
                fallbackFactoryCalls++;
                throw new InvalidOperationException(
                    "Fallback must remain lazy.");
            });

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(RuntimeMacroState.Active, Assert.Single(status.Macros).State);
        Assert.Equal(0, fallbackFactoryCalls);
        Assert.Single(native.RegisterCalls);
    }

    [Fact]
    public void NonCollisionNativeFailureDoesNotCreateFallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailNextRegister(nativeErrorCode: 87);
        using var service = new WindowsGlobalHotkeyService(native);
        var fallbackFactoryCalls = 0;
        using var coordinator = Coordinator(
            Settings(KeyboardMacro(
                1,
                AltR(),
                KeyboardKey.A)),
            service,
            new RecordingKeyboardOutput(),
            fallbackFactory: () =>
            {
                fallbackFactoryCalls++;
                throw new InvalidOperationException("Must not be called.");
            });

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(
            MultiMacroRuntimeState.NoRegistrationsActive,
            status.State);
        Assert.Equal(
            RuntimeMacroState.RegistrationFailed,
            Assert.Single(status.Macros).State);
        Assert.Equal(0, fallbackFactoryCalls);
    }

    [Fact]
    public async Task BuyAllAmmoAltRCollisionUsesFallbackAsActiveAndNoRepeat()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailNextRegister(ErrorHotkeyAlreadyRegistered);
        var nativeScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(
            native,
            nativeScheduler);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = Coordinator(
            Settings(KeyboardMacro(
                1,
                AltR(),
                KeyboardKey.A,
                name: "Buy All Ammo")),
            service,
            output,
            () => fallback);

        MultiMacroRuntimeStatus status = coordinator.Start();
        PressAltR(fallbackNative);
        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        ReleaseAltR(fallbackNative);
        Assert.Empty(output.Snapshot());
        Assert.Equal(1, fallbackScheduler.QueuedCount);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(RuntimeMacroState.Active, Assert.Single(status.Macros).State);
        Assert.Equal(new[] { "Down:A", "Up:A" }, output.Snapshot());

        PressAltR(fallbackNative);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);

        Assert.Equal(
            new[] { "Down:A", "Up:A", "Down:A", "Up:A" },
            output.Snapshot());
    }

    [Fact]
    public async Task ActualFactoryKeyboardFallbacksKeepBuyActiveAndRouteOnlyRequestSparrow()
    {
        ApplicationSettings defaults = StarterConfiguration.Default;
        MacroDefinition buyAllAmmo = defaults.Macros.Single(
            macro => macro.Name == "Buy All Ammo");
        MacroDefinition requestSparrow = defaults.Macros.Single(
            macro => macro.Name == "Request Sparrow");
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailNextRegister(ErrorHotkeyAlreadyRegistered);
        native.FailNextRegister(ErrorHotkeyAlreadyRegistered);
        using var service = new WindowsGlobalHotkeyService(native);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = Coordinator(
            Settings(buyAllAmmo, requestSparrow),
            service,
            output,
            () => fallback);

        MultiMacroRuntimeStatus status = coordinator.Start();
        PressControlAltH(fallbackNative);
        ReleaseControlAltH(fallbackNative);
        await fallbackScheduler.DrainAsync();
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);

        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.All(
            status.Macros,
            macro => Assert.Equal(RuntimeMacroState.Active, macro.State));
        Assert.Equal(1, fallbackNative.InstallCallCount);
        Assert.Equal(
            ExpectedKeyboardEvents(requestSparrow.Sequence),
            output.Snapshot());
    }

    [Fact]
    public async Task MixedNativeFallbackAndMouseBindingsAllActivateAndRouteByMacroId()
    {
        using var keyboardNative = new FakeGlobalHotkeyNativeApi();
        keyboardNative.FailRegisterCall(2, ErrorHotkeyAlreadyRegistered);
        var keyboardScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var keyboardService = new WindowsGlobalHotkeyService(
            keyboardNative,
            keyboardScheduler);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallbackService = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        using var mouseNative = new FakeGlobalMouseButtonNativeApi();
        var mouseScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var mouseService = new WindowsGlobalMouseButtonService(
            mouseNative,
            mouseScheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = new MultiMacroHotkeyCoordinator(
            Settings(
                KeyboardMacro(
                    1,
                    new KeyboardHotkey(HotkeyKey.F8),
                    KeyboardKey.A),
                KeyboardMacro(2, AltR(), KeyboardKey.B),
                MouseMacro(3, MouseButton.XButton1, KeyboardKey.C)),
            () => keyboardService,
            new MacroRunner(output, new ImmediateDelay()),
            mouseButtonServiceFactory: () => mouseService,
            keyboardFallbackServiceFactory: () => fallbackService);

        MultiMacroRuntimeStatus status = coordinator.Start();
        await TriggerNativeAsync(
            keyboardNative,
            keyboardScheduler,
            HotkeyKey.F8);
        await output.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);
        PressAltR(fallbackNative);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);
        mouseNative.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown,
            xButton: 1);
        mouseNative.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonUp,
            xButton: 1);
        await mouseScheduler.DrainAsync();
        await output.WaitForCallCountAsync(6).WaitAsync(SafetyTimeout);

        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.All(
            status.Macros,
            macro => Assert.Equal(RuntimeMacroState.Active, macro.State));
        Assert.Equal(
            new[]
            {
                "Down:A", "Up:A",
                "Down:B", "Up:B",
                "Down:C", "Up:C"
            },
            output.Snapshot());
    }

    [Fact]
    public async Task MultipleFallbackHotkeysRouteOnlyTheirOwnMacro()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(1, ErrorHotkeyAlreadyRegistered);
        native.FailRegisterCall(2, ErrorHotkeyAlreadyRegistered);
        using var service = new WindowsGlobalHotkeyService(native);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = Coordinator(
            Settings(
                KeyboardMacro(1, AltR(), KeyboardKey.A),
                KeyboardMacro(
                    2,
                    new KeyboardHotkey(
                        HotkeyKey.H,
                        HotkeyModifiers.Control | HotkeyModifiers.Alt),
                    KeyboardKey.B)),
            service,
            output,
            () => fallback);
        MultiMacroRuntimeStatus status = coordinator.Start();

        PressAltR(fallbackNative);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        ReleaseAltR(fallbackNative);
        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftControl);
        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyH,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);

        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(1, fallbackNative.InstallCallCount);
        Assert.Equal(
            new[] { "Down:A", "Up:A", "Down:B", "Up:B" },
            output.Snapshot());
    }

    [Fact]
    public async Task InjectedAltRDoesNotEnterCoordinatorExecutionPath()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailNextRegister(ErrorHotkeyAlreadyRegistered);
        using var service = new WindowsGlobalHotkeyService(native);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = Coordinator(
            Settings(KeyboardMacro(1, AltR(), KeyboardKey.A)),
            service,
            output,
            () => fallback);
        coordinator.Start();
        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);

        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown |
                FakeGlobalKeyboardFallbackNativeApi.Injected);
        fallbackNative.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown |
                FakeGlobalKeyboardFallbackNativeApi.Injected);
        await fallbackScheduler.DrainAsync();

        Assert.Empty(output.Snapshot());
    }

    [Fact]
    public void FallbackInstallFailureIsIsolatedFromSuccessfulNativeMacro()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(2, ErrorHotkeyAlreadyRegistered);
        using var service = new WindowsGlobalHotkeyService(native);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        fallbackNative.FailNextInstall(errorCode: 1428);
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative);
        using var coordinator = Coordinator(
            Settings(
                KeyboardMacro(
                    1,
                    new KeyboardHotkey(HotkeyKey.F8),
                    KeyboardKey.A),
                KeyboardMacro(2, AltR(), KeyboardKey.B)),
            service,
            new RecordingKeyboardOutput(),
            () => fallback);

        MultiMacroRuntimeStatus status = coordinator.Start();

        Assert.Equal(MultiMacroRuntimeState.PartiallyActive, status.State);
        Assert.Equal(RuntimeMacroState.Active, status.Macros[0].State);
        Assert.Equal(RuntimeMacroState.RegistrationFailed, status.Macros[1].State);
        Assert.Contains("fallback", status.Macros[1].Diagnostic!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallbackPressWhileBusyIsDroppedAndDoesNotStartAfterRelease()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(2, ErrorHotkeyAlreadyRegistered);
        var nativeScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(
            native,
            nativeScheduler);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        var output = new RecordingKeyboardOutput();
        var delay = new ControlledDelay();
        using var coordinator = new MultiMacroHotkeyCoordinator(
            Settings(
                KeyboardMacro(
                    1,
                    new KeyboardHotkey(HotkeyKey.F8),
                    new MacroSequence(new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.A),
                        new DelayStep(TimeSpan.FromMilliseconds(1)),
                        new KeyTapStep(KeyboardKey.B)
                    })),
                KeyboardMacro(2, AltR(), KeyboardKey.C)),
            () => service,
            new MacroRunner(output, delay),
            keyboardFallbackServiceFactory: () => fallback);
        coordinator.Start();

        await TriggerNativeAsync(native, nativeScheduler, HotkeyKey.F8);
        await delay.Entered.Task.WaitAsync(SafetyTimeout);
        PressAltR(fallbackNative);
        await fallbackScheduler.DrainAsync();

        delay.Release();
        await output.WaitForCallCountAsync(4).WaitAsync(SafetyTimeout);
        await coordinator.WaitForIdleForTestingAsync().WaitAsync(SafetyTimeout);
        Assert.Equal(
            new[] { "Down:A", "Up:A", "Down:B", "Up:B" },
            output.Snapshot());

        ReleaseAltR(fallbackNative);
        PressAltR(fallbackNative);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(6).WaitAsync(SafetyTimeout);
        Assert.Equal(
            new[]
            {
                "Down:A", "Up:A", "Down:B", "Up:B",
                "Down:C", "Up:C"
            },
            output.Snapshot());
    }

    [Fact]
    public async Task FallbackTerminalFailureOnlyDeactivatesFallbackBindings()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(2, ErrorHotkeyAlreadyRegistered);
        var nativeScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(
            native,
            nativeScheduler);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative);
        var output = new RecordingKeyboardOutput();
        using var coordinator = Coordinator(
            Settings(
                KeyboardMacro(
                    1,
                    new KeyboardHotkey(HotkeyKey.F8),
                    KeyboardKey.A),
                KeyboardMacro(2, AltR(), KeyboardKey.B)),
            service,
            output,
            () => fallback);
        coordinator.Start();
        var changed = new TaskCompletionSource<MultiMacroRuntimeStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.PartiallyActive)
            {
                changed.TrySetResult(status);
            }
        };

        fallbackNative.FailNextGetMessage();
        MultiMacroRuntimeStatus terminal = await changed.Task.WaitAsync(
            SafetyTimeout);
        await TriggerNativeAsync(native, nativeScheduler, HotkeyKey.F8);
        await output.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);

        Assert.Equal(RuntimeMacroState.Active, terminal.Macros[0].State);
        Assert.Equal(RuntimeMacroState.RuntimeUnavailable, terminal.Macros[1].State);
        Assert.Equal(new[] { "Down:A", "Up:A" }, output.Snapshot());
        coordinator.Dispose();
    }

    [Fact]
    public async Task NativeTerminalFailureLeavesFallbackBindingUsable()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        native.FailRegisterCall(2, ErrorHotkeyAlreadyRegistered);
        var service = new WindowsGlobalHotkeyService(native);
        using var fallbackNative = new FakeGlobalKeyboardFallbackNativeApi();
        var fallbackScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var fallback = new WindowsGlobalKeyboardFallbackService(
            fallbackNative,
            fallbackScheduler);
        var output = new RecordingKeyboardOutput();
        using var coordinator = Coordinator(
            Settings(
                KeyboardMacro(
                    1,
                    new KeyboardHotkey(HotkeyKey.F8),
                    KeyboardKey.A),
                KeyboardMacro(2, AltR(), KeyboardKey.B)),
            service,
            output,
            () => fallback);
        coordinator.Start();
        var changed = new TaskCompletionSource<MultiMacroRuntimeStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StatusChanged += status =>
        {
            if (status.State == MultiMacroRuntimeState.PartiallyActive)
            {
                changed.TrySetResult(status);
            }
        };

        native.FailNextGetMessage();
        MultiMacroRuntimeStatus terminal = await changed.Task.WaitAsync(
            SafetyTimeout);
        PressAltR(fallbackNative);
        await fallbackScheduler.DrainAsync();
        await output.WaitForCallCountAsync(2).WaitAsync(SafetyTimeout);

        Assert.Equal(RuntimeMacroState.RuntimeUnavailable, terminal.Macros[0].State);
        Assert.Equal(RuntimeMacroState.Active, terminal.Macros[1].State);
        Assert.Equal(new[] { "Down:B", "Up:B" }, output.Snapshot());
    }

    private static MultiMacroHotkeyCoordinator Coordinator(
        ApplicationSettings settings,
        WindowsGlobalHotkeyService service,
        IKeyboardOutput output,
        Func<WindowsGlobalKeyboardFallbackService> fallbackFactory)
    {
        return new MultiMacroHotkeyCoordinator(
            settings,
            () => service,
            new MacroRunner(output, new ImmediateDelay()),
            keyboardFallbackServiceFactory: fallbackFactory);
    }

    private static KeyboardHotkey AltR() =>
        new(HotkeyKey.R, HotkeyModifiers.Alt);

    private static ApplicationSettings Settings(
        params MacroDefinition[] macros) => new(macros);

    private static MacroDefinition KeyboardMacro(
        int id,
        KeyboardHotkey hotkey,
        KeyboardKey output,
        string? name = null) => KeyboardMacro(
            id,
            hotkey,
            new MacroSequence(new MacroStep[] { new KeyTapStep(output) }),
            name);

    private static MacroDefinition KeyboardMacro(
        int id,
        KeyboardHotkey hotkey,
        MacroSequence sequence,
        string? name = null) => new(
            Id(id),
            name ?? $"Keyboard {id}",
            enabled: true,
            new KeyboardMacroTrigger(hotkey),
            MacroExecutionMode.Once,
            sequence);

    private static MacroDefinition MouseMacro(
        int id,
        MouseButton button,
        KeyboardKey output) => new(
            Id(id),
            $"Mouse {id}",
            enabled: true,
            new MouseButtonMacroTrigger(button),
            MacroExecutionMode.Once,
            new MacroSequence(new MacroStep[] { new KeyTapStep(output) }));

    private static MacroId Id(int value) =>
        new(new Guid(value, 0, 0, new byte[8]));

    private static async Task TriggerNativeAsync(
        FakeGlobalHotkeyNativeApi native,
        FakeHotkeyTriggerWorkScheduler scheduler,
        HotkeyKey key)
    {
        uint virtualKey = key switch
        {
            HotkeyKey.F8 => 0x77,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        int registrationId = native.RegisterCalls
            .Single(call => call.VirtualKey == virtualKey)
            .RegistrationId;
        native.EnqueueHotkey(registrationId);
        await scheduler.WaitForQueuedCountAsync(1).WaitAsync(SafetyTimeout);
        await scheduler.RunNextAsync().WaitAsync(SafetyTimeout);
    }

    private static void PressAltR(FakeGlobalKeyboardFallbackNativeApi native)
    {
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
    }

    private static void ReleaseAltR(FakeGlobalKeyboardFallbackNativeApi native)
    {
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt);
    }

    private static void PressControlAltH(FakeGlobalKeyboardFallbackNativeApi native)
    {
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftControl);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyH,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
    }

    private static void ReleaseControlAltH(FakeGlobalKeyboardFallbackNativeApi native)
    {
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyH,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftControl);
    }

    private static IReadOnlyList<string> ExpectedKeyboardEvents(
        MacroSequence sequence) => sequence.Steps
            .OfType<KeyTapStep>()
            .SelectMany(step => new[]
            {
                $"Down:{step.Key}",
                $"Up:{step.Key}"
            })
            .ToArray();

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private readonly object _gate = new();
        private readonly List<CountWaiter> _waiters = new();

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

        private void Record(string value)
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
}
