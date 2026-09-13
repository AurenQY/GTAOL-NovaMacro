using System.ComponentModel;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalMouseButtonServiceTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public void Register_TwoButtonsShareOneHookAndIndependentHandles()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        using var service = new WindowsGlobalMouseButtonService(native);

        GlobalMouseButtonRegistration first = service.Register(MouseButton.XButton1);
        GlobalMouseButtonRegistration second = service.Register(MouseButton.XButton2);

        Assert.Equal(1, native.InstallCallCount);
        Assert.Equal(MouseButton.XButton1, first.Button);
        Assert.Equal(MouseButton.XButton2, second.Button);
        Assert.Throws<InvalidOperationException>(
            () => service.Register(MouseButton.XButton1));

        first.Dispose();
        Assert.Empty(native.RemovalCalls);
        second.Dispose();
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
        first.Dispose();
        second.Dispose();
    }

    [Theory]
    [InlineData(MouseButton.XButton1, (ushort)1)]
    [InlineData(MouseButton.XButton2, (ushort)2)]
    public async Task RegisteredButton_DownAndUpAreSwallowedAndPublishedInOrder(
        MouseButton button,
        ushort nativeButton)
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using GlobalMouseButtonRegistration registration = service.Register(button);
        var observed = new List<string>();
        service.ButtonDown += value => observed.Add($"Down:{value}");
        service.ButtonUp += value => observed.Add($"Up:{value}");

        nint downResult = native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, nativeButton);
        nint upResult = native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonUp, nativeButton);
        await scheduler.DrainAsync();

        Assert.Equal((nint)1, downResult);
        Assert.Equal((nint)1, upResult);
        Assert.Equal(new[] { $"Down:{button}", $"Up:{button}" }, observed);
        Assert.Equal(0, native.CallNextCount);
    }

    [Fact]
    public async Task DuplicateDownAndStrayUpAreSwallowedWithoutDuplicateCallbacks()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        using GlobalMouseButtonRegistration registration =
            service.Register(MouseButton.XButton1);
        var observed = new List<string>();
        service.ButtonDown += _ => observed.Add("Down");
        service.ButtonUp += _ => observed.Add("Up");

        Assert.Equal((nint)1, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1));
        Assert.Equal((nint)1, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1));
        Assert.Equal((nint)1, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1));
        Assert.Equal((nint)1, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1));
        Assert.Equal((nint)1, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonUp, 1));
        await scheduler.DrainAsync();

        Assert.Equal(new[] { "Down", "Up" }, observed);
    }

    [Fact]
    public void UnregisteredAndNonXButtonMessagesPassToNextHook()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        using var service = new WindowsGlobalMouseButtonService(native);
        using GlobalMouseButtonRegistration registration =
            service.Register(MouseButton.XButton1);

        Assert.Equal((nint)5678, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, 2));
        Assert.Equal((nint)5678, native.InvokeHook(0x0200, 1));
        Assert.Equal((nint)5678, native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1, code: -1));
        Assert.Equal(3, native.CallNextCount);
    }

    [Fact]
    public async Task RegistrationDisposalInvalidatesQueuedAndFutureCallbacks()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        GlobalMouseButtonRegistration registration =
            service.Register(MouseButton.XButton1);
        var callbackCount = 0;
        service.ButtonDown += _ => callbackCount++;

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        registration.Dispose();
        await scheduler.DrainAsync();
        nint lateResult = native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);

        Assert.Equal(0, callbackCount);
        Assert.Equal((nint)5678, lateResult);
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
    }

    [Fact]
    public async Task SubscriberFailureIsIsolatedAndReentrantHandleDisposeDoesNotDeadlock()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalMouseButtonService(native, scheduler);
        GlobalMouseButtonRegistration registration =
            service.Register(MouseButton.XButton1);
        var secondSubscriberCalled = false;
        service.ButtonDown += _ => throw new InvalidOperationException("subscriber");
        service.ButtonDown += _ =>
        {
            secondSubscriberCalled = true;
            registration.Dispose();
        };

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.RunNextAsync().WaitAsync(SafetyTimeout);

        Assert.True(secondSubscriberCalled);
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
    }

    [Fact]
    public async Task SubscriberCanDisposeServiceWithoutDeadlockOrLaterSubscriber()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalMouseButtonService(native, scheduler);
        service.Register(MouseButton.XButton1);
        var laterSubscriberCalled = false;
        service.ButtonDown += _ => service.Dispose();
        service.ButtonDown += _ => laterSubscriberCalled = true;

        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);
        await scheduler.RunNextAsync().WaitAsync(SafetyTimeout);

        Assert.False(laterSubscriberCalled);
        Assert.Null(await service.MessageLoopCompletion.WaitAsync(SafetyTimeout));
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
        service.Dispose();
    }

    [Fact]
    public void HookInstallationFailurePreservesButtonAndNativeError()
    {
        const int errorCode = 1428;
        using var native = new FakeGlobalMouseButtonNativeApi();
        native.FailNextInstall(errorCode);
        using var service = new WindowsGlobalMouseButtonService(native);

        GlobalMouseButtonRegistrationException exception = Assert.Throws<
            GlobalMouseButtonRegistrationException>(
                () => service.Register(MouseButton.XButton2));

        Assert.Equal(MouseButton.XButton2, exception.Button);
        Assert.Equal(errorCode, exception.NativeErrorCode);
        Assert.Empty(native.RemovalCalls);
    }

    [Fact]
    public async Task ServiceDisposeUnhooksInvalidatesHandlesAndCutsOffQueuedCallbacks()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalMouseButtonService(native, scheduler);
        GlobalMouseButtonRegistration registration =
            service.Register(MouseButton.XButton1);
        var callbackCount = 0;
        service.ButtonDown += _ => callbackCount++;
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);

        service.Dispose();
        await scheduler.DrainAsync();
        registration.Dispose();
        nint lateResult = native.InvokeHook(
            FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);

        Assert.Equal(0, callbackCount);
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
        Assert.Null(await service.MessageLoopCompletion.WaitAsync(SafetyTimeout));
        Assert.Equal((nint)5678, lateResult);
        Assert.Throws<ObjectDisposedException>(
            () => service.Register(MouseButton.XButton1));
    }

    [Fact]
    public async Task UnexpectedMessageLoopFailureBecomesTerminalAndUnhooks()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var service = new WindowsGlobalMouseButtonService(native);
        GlobalMouseButtonRegistration registration =
            service.Register(MouseButton.XButton1);

        native.FailNextGetMessage();
        Exception? failure = await service.MessageLoopCompletion.WaitAsync(
            SafetyTimeout);

        Assert.IsType<Win32Exception>(failure);
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
        registration.Dispose();
        Assert.ThrowsAny<Exception>(service.Dispose);
    }

    [Fact]
    public async Task UnexpectedQuitCutsOffQueuedCallbackAndBecomesTerminal()
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalMouseButtonService(native, scheduler);
        service.Register(MouseButton.XButton1);
        var callbackCount = 0;
        service.ButtonDown += _ => callbackCount++;
        native.InvokeHook(FakeGlobalMouseButtonNativeApi.WmXButtonDown, 1);

        native.EnqueueUnexpectedQuit();
        Exception? failure = await service.MessageLoopCompletion.WaitAsync(
            SafetyTimeout);
        await scheduler.DrainAsync();

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, callbackCount);
        Assert.Equal(new[] { (nint)1234 }, native.RemovalCalls);
        Assert.ThrowsAny<Exception>(service.Dispose);
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData((MouseButton)99)]
    public void InvalidTriggerButtonFailsBeforeHookInstallation(MouseButton button)
    {
        using var native = new FakeGlobalMouseButtonNativeApi();
        using var service = new WindowsGlobalMouseButtonService(native);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Register(button));
        Assert.Equal(0, native.InstallCallCount);
    }
}
