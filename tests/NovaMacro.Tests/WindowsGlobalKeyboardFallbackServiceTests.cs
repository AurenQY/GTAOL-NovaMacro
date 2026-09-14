using System.ComponentModel;
using System.Runtime.InteropServices;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalKeyboardFallbackServiceTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public void LowLevelKeyboardNativeLayoutMatchesWindowsAbi()
    {
        Assert.Equal(13, NativeMethods.WhKeyboardLowLevel);
        Assert.Equal(IntPtr.Size == 8 ? 24 : 20,
            Marshal.SizeOf<NativeMethods.LowLevelKeyboardHookData>());
    }

    [Fact]
    public void MultipleRegistrationsShareOneHookAndIndependentHandles()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        using var service = new WindowsGlobalKeyboardFallbackService(native);
        var altR = new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt);
        var controlAltH = new KeyboardHotkey(
            HotkeyKey.H,
            HotkeyModifiers.Control | HotkeyModifiers.Alt);

        GlobalKeyboardFallbackRegistration first = service.Register(altR);
        GlobalKeyboardFallbackRegistration second = service.Register(controlAltH);

        Assert.Equal(1, native.InstallCallCount);
        Assert.Equal(altR, first.Hotkey);
        Assert.Equal(controlAltH, second.Hotkey);
        Assert.Throws<InvalidOperationException>(() => service.Register(altR));

        first.Dispose();
        Assert.Empty(native.RemovalCalls);
        second.Dispose();
        Assert.Equal(new[] { (nint)2468 }, native.RemovalCalls);
    }

    [Fact]
    public async Task AltSysKeyPathPassesThroughSuppressesRepeatAndAllowsSecondPress()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var observed = new List<KeyboardHotkey>();
        service.Triggered += observed.Add;

        Assert.Equal((nint)8642, native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown));
        Assert.Equal((nint)8642, native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown));
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        await scheduler.DrainAsync();

        Assert.Single(observed);

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyRightAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        await scheduler.DrainAsync();

        Assert.Equal(2, observed.Count);
        Assert.Equal(7, native.CallNextCount);
    }

    [Fact]
    public async Task StrayTargetUpDoesNotTriggerOrPoisonTheNextPhysicalPress()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var observed = 0;
        service.Triggered += _ => observed++;

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        await scheduler.DrainAsync();

        Assert.Equal(0, observed);

        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR);
        await scheduler.DrainAsync();

        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task MultipleModifierRegistrationsRouteExactlyWithoutCrossTrigger()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        var altR = new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt);
        var controlAltH = new KeyboardHotkey(
            HotkeyKey.H,
            HotkeyModifiers.Control | HotkeyModifiers.Alt);
        using GlobalKeyboardFallbackRegistration first = service.Register(altR);
        using GlobalKeyboardFallbackRegistration second =
            service.Register(controlAltH);
        var observed = new List<KeyboardHotkey>();
        service.Triggered += observed.Add;

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyRightControl);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyH);
        await scheduler.DrainAsync();

        Assert.Equal(new[] { altR, controlAltH }, observed);
    }

    [Fact]
    public async Task LeftAndRightShiftOrControlProduceTheSameLogicalModifiers()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(
                HotkeyKey.H,
                HotkeyModifiers.Control | HotkeyModifiers.Shift));
        var observed = 0;
        service.Triggered += _ => observed++;

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftControl);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyRightShift);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyH);
        await scheduler.DrainAsync();

        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task AdditionalModifierPreventsFallbackOnlyPartialMatch()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var observed = 0;
        service.Triggered += _ => observed++;

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftControl);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftControl);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR);

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftShift);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftShift);
        PressAndRelease(native, FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR);
        await scheduler.DrainAsync();

        Assert.Equal(2, observed);
    }

    [Theory]
    [InlineData(HotkeyKey.A, 0x41u)]
    [InlineData(HotkeyKey.F11, 0x7Au)]
    [InlineData(HotkeyKey.Numpad7, 0x67u)]
    [InlineData(HotkeyKey.NumpadDivide, 0x6Fu)]
    public async Task ExistingTriggerVocabularyWorksThroughSharedMapper(
        HotkeyKey key,
        uint virtualKey)
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(key));
        var observed = 0;
        service.Triggered += _ => observed++;

        PressAndRelease(native, virtualKey);
        await scheduler.DrainAsync();

        Assert.Equal(1, observed);
    }

    [Theory]
    [InlineData(FakeGlobalKeyboardFallbackNativeApi.Injected)]
    [InlineData(FakeGlobalKeyboardFallbackNativeApi.LowerIntegrityInjected)]
    [InlineData(
        FakeGlobalKeyboardFallbackNativeApi.Injected |
        FakeGlobalKeyboardFallbackNativeApi.LowerIntegrityInjected)]
    public async Task InjectedMatchingInputIsIgnored(uint injectedFlags)
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var observed = 0;
        service.Triggered += _ => observed++;
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyLeftAlt,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);

        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown | injectedFlags);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown | injectedFlags);
        await scheduler.DrainAsync();

        Assert.Equal(0, observed);
        Assert.Equal(3, native.CallNextCount);
    }

    [Fact]
    public void NegativeCodeUnknownMessageAndUnregisteredKeyPassThrough()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        using var service = new WindowsGlobalKeyboardFallbackService(native);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));

        Assert.Equal((nint)8642, native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            code: -1));
        Assert.Equal((nint)8642, native.InvokeHook(
            0x9999,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR));
        Assert.Equal((nint)8642, native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyH));
    }

    [Fact]
    public void HookInstallationFailurePreservesHotkeyAndNativeError()
    {
        const int errorCode = 1428;
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        native.FailNextInstall(errorCode);
        using var service = new WindowsGlobalKeyboardFallbackService(native);
        var hotkey = new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt);

        GlobalKeyboardFallbackRegistrationException exception = Assert.Throws<
            GlobalKeyboardFallbackRegistrationException>(
                () => service.Register(hotkey));

        Assert.Equal(hotkey, exception.Hotkey);
        Assert.Equal(errorCode, exception.NativeErrorCode);
        Assert.Empty(native.RemovalCalls);
    }

    [Fact]
    public async Task RegistrationDisposalCutsOffQueuedAndFutureCallbacks()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var observed = 0;
        service.Triggered += _ => observed++;
        PressAltR(native);

        registration.Dispose();
        await scheduler.DrainAsync();
        nint lateResult = native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);

        Assert.Equal(0, observed);
        Assert.Equal((nint)8642, lateResult);
        Assert.Equal(new[] { (nint)2468 }, native.RemovalCalls);
    }

    [Fact]
    public async Task SubscriberCanDisposeServiceWithoutDeadlockOrLaterSubscriber()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalKeyboardFallbackService(native, scheduler);
        service.Register(new KeyboardHotkey(
            HotkeyKey.R,
            HotkeyModifiers.Alt));
        var laterSubscriberCalled = false;
        service.Triggered += _ => service.Dispose();
        service.Triggered += _ => laterSubscriberCalled = true;
        PressAltR(native);

        await scheduler.RunNextAsync().WaitAsync(SafetyTimeout);

        Assert.False(laterSubscriberCalled);
        Assert.Null(await service.MessageLoopCompletion.WaitAsync(SafetyTimeout));
        Assert.Equal(new[] { (nint)2468 }, native.RemovalCalls);
        service.Dispose();
    }

    [Fact]
    public async Task SubscriberFailureIsContainedAndLaterSubscriberStillRuns()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        using GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var laterSubscriberCalled = false;
        service.Triggered += _ =>
            throw new InvalidOperationException("controlled subscriber failure");
        service.Triggered += _ => laterSubscriberCalled = true;
        PressAltR(native);

        await scheduler.DrainAsync();

        Assert.True(laterSubscriberCalled);
    }

    [Fact]
    public async Task InFlightSubscriberDoesNotBlockRegistrationDisposal()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalKeyboardFallbackService(
            native,
            scheduler);
        GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var laterSubscriberCalled = false;
        service.Triggered += _ =>
        {
            entered.TrySetResult();
            release.Wait(SafetyTimeout);
        };
        service.Triggered += _ => laterSubscriberCalled = true;
        PressAltR(native);
        Task callback = Task.Run(scheduler.RunNextAsync);
        await entered.Task.WaitAsync(SafetyTimeout);

        Task disposal = Task.Run(registration.Dispose);
        await disposal.WaitAsync(SafetyTimeout);
        release.Set();
        await callback.WaitAsync(SafetyTimeout);

        Assert.False(laterSubscriberCalled);
    }

    [Fact]
    public async Task ServiceDisposeUnhooksInvalidatesAndCutsOffQueuedCallbacks()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalKeyboardFallbackService(native, scheduler);
        GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));
        var observed = 0;
        service.Triggered += _ => observed++;
        PressAltR(native);

        service.Dispose();
        await scheduler.DrainAsync();
        registration.Dispose();
        nint lateDown = native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyDown,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);
        nint lateUp = native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmSysKeyUp,
            FakeGlobalKeyboardFallbackNativeApi.VirtualKeyR,
            FakeGlobalKeyboardFallbackNativeApi.AltDown);

        Assert.Equal(0, observed);
        Assert.Equal((nint)8642, lateDown);
        Assert.Equal((nint)8642, lateUp);
        Assert.Equal(new[] { (nint)2468 }, native.RemovalCalls);
        Assert.Throws<ObjectDisposedException>(
            () => service.Register(new KeyboardHotkey(HotkeyKey.F8)));
    }

    [Fact]
    public async Task UnexpectedMessageLoopFailureIsTerminalAndUnhooks()
    {
        using var native = new FakeGlobalKeyboardFallbackNativeApi();
        var service = new WindowsGlobalKeyboardFallbackService(native);
        GlobalKeyboardFallbackRegistration registration = service.Register(
            new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt));

        native.FailNextGetMessage();
        Exception? failure = await service.MessageLoopCompletion.WaitAsync(
            SafetyTimeout);

        Assert.IsType<Win32Exception>(failure);
        Assert.Equal(new[] { (nint)2468 }, native.RemovalCalls);
        registration.Dispose();
        Assert.ThrowsAny<Exception>(service.Dispose);
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

    private static void PressAndRelease(
        FakeGlobalKeyboardFallbackNativeApi native,
        uint virtualKey)
    {
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyDown,
            virtualKey);
        native.InvokeHook(
            FakeGlobalKeyboardFallbackNativeApi.WmKeyUp,
            virtualKey);
    }
}
