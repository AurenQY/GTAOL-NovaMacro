using System.ComponentModel;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalHotkeyServiceLifecycleTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public void Register_FromNewServiceRegistersExpectedNativeHotkey()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var hotkey = new KeyboardHotkey(HotkeyKey.F8);

        service.Register(hotkey);

        FakeGlobalHotkeyNativeApi.RegisterHotkeyCall call =
            Assert.Single(native.RegisterCalls);
        Assert.Equal(1, call.RegistrationId);
        Assert.Equal(0x4000u, call.Modifiers);
        Assert.Equal(0x77u, call.VirtualKey);
    }

    [Fact]
    public void Register_DistinctHotkeysCreatesIndependentRegistrations()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var firstHotkey = new KeyboardHotkey(HotkeyKey.F8);
        var secondHotkey = new KeyboardHotkey(HotkeyKey.F9);

        using GlobalHotkeyRegistration first = service.Register(firstHotkey);
        using GlobalHotkeyRegistration second = service.Register(secondHotkey);

        Assert.NotEqual(first.RegistrationId, second.RegistrationId);
        Assert.Equal(firstHotkey, first.Hotkey);
        Assert.Equal(secondHotkey, second.Hotkey);
        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));
    }

    [Fact]
    public void Unregister_WithoutRegistrationIsSafeNoOp()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);

        service.Unregister();

        Assert.Empty(native.RegisterCalls);
        Assert.Empty(native.UnregisterCalls);
    }

    [Fact]
    public void Unregister_RemovesActiveRegistration()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        service.Unregister();

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
    }

    [Fact]
    public void Register_AfterUnregisterSucceedsWithFreshId()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        service.Unregister();

        service.Register(new KeyboardHotkey(HotkeyKey.A));

        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));
    }

    [Fact]
    public void Replace_WithoutRegistrationThrows()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.Replace(new KeyboardHotkey(HotkeyKey.F8)));

        Assert.Contains("No hotkey is registered", exception.Message);
        Assert.Empty(native.RegisterCalls);
        Assert.Empty(native.UnregisterCalls);
    }

    [Fact]
    public void Replace_WithSameValueIsNativeNoOp()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control));

        service.Replace(new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control));

        Assert.Single(native.RegisterCalls);
        Assert.Empty(native.UnregisterCalls);
    }

    [Fact]
    public void Replace_SuccessUsesFreshIdAndUnregistersOldId()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        service.Replace(new KeyboardHotkey(
            HotkeyKey.A,
            HotkeyModifiers.Control | HotkeyModifiers.Shift));

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));
        FakeGlobalHotkeyNativeApi.RegisterHotkeyCall newRegistration =
            native.RegisterCalls.Last();
        Assert.Equal(0x41u, newRegistration.VirtualKey);
        Assert.Equal(0x4006u, newRegistration.Modifiers);
    }

    [Fact]
    public void Replace_WhenOldUnregisterFailsKeepsOldRegistrationCurrent()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var oldHotkey = new KeyboardHotkey(HotkeyKey.F8);
        service.Register(oldHotkey);
        native.FailNextUnregister();

        Win32Exception exception = Assert.Throws<Win32Exception>(
            () => service.Replace(new KeyboardHotkey(HotkeyKey.A)));

        Assert.Contains("Failed to unregister", exception.Message);
        Assert.Single(native.RegisterCalls);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);

        service.Replace(new KeyboardHotkey(HotkeyKey.F8));
        Assert.Single(native.RegisterCalls);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
    }

    [Fact]
    public void Replace_WhenNewRegisterFailsLeavesServiceUnregisteredAndConsumesId()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        native.FailNextRegister();

        GlobalHotkeyRegistrationException exception =
            Assert.Throws<GlobalHotkeyRegistrationException>(
                () => service.Replace(new KeyboardHotkey(HotkeyKey.A)));

        Assert.Contains("Windows rejected global hotkey", exception.Message);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);

        service.Register(new KeyboardHotkey(HotkeyKey.B));

        Assert.Equal(
            new[] { 1, 2, 3 },
            native.RegisterCalls.Select(call => call.RegistrationId));
    }

    [Fact]
    public void FailedRegisterAttemptConsumesRegistrationId()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        native.FailNextRegister();

        Assert.Throws<GlobalHotkeyRegistrationException>(
            () => service.Register(new KeyboardHotkey(HotkeyKey.F8)));

        service.Register(new KeyboardHotkey(HotkeyKey.A));

        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));
    }

    [Fact]
    public void Register_WhenNativeRejectsCarriesHotkeyAndNativeErrorCode()
    {
        const int expectedNativeErrorCode = 1409;
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var hotkey = new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control | HotkeyModifiers.Shift);
        native.FailNextRegister(expectedNativeErrorCode);

        GlobalHotkeyRegistrationException exception =
            Assert.Throws<GlobalHotkeyRegistrationException>(
                () => service.Register(hotkey));

        Assert.Equal(hotkey, exception.Hotkey);
        Assert.Equal(expectedNativeErrorCode, exception.NativeErrorCode);
    }

    [Fact]
    public void Dispose_UnregistersActiveRegistrationExactlyOnce()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        service.Dispose();
        service.Dispose();

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
    }

    [Fact]
    public void LifecycleMethods_AfterCompletedDisposeThrowObjectDisposedException()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => service.Register(new KeyboardHotkey(HotkeyKey.F8)));
        Assert.Throws<ObjectDisposedException>(
            () => service.Replace(new KeyboardHotkey(HotkeyKey.F8)));
        Assert.Throws<ObjectDisposedException>(() => service.Unregister());
        service.Dispose();
    }

    [Fact]
    public async Task Register_WhenCommandPostFailsSurfacesFailureAndCanRecover()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        native.FailNextPostThreadMessage();

        Exception failure = await Task.Run(
                () => CaptureException(
                    () => service.Register(new KeyboardHotkey(HotkeyKey.F8))))
            .WaitAsync(SafetyTimeout) ?? throw new Xunit.Sdk.XunitException(
                "Expected PostThreadMessage failure.");

        Win32Exception win32Failure = Assert.IsType<Win32Exception>(failure);
        Assert.Contains("Failed to send a command", win32Failure.Message);
        Assert.Empty(native.RegisterCalls);

        service.Register(new KeyboardHotkey(HotkeyKey.A));

        FakeGlobalHotkeyNativeApi.RegisterHotkeyCall call =
            Assert.Single(native.RegisterCalls);
        Assert.Equal(1, call.RegistrationId);
        Assert.Equal(0x41u, call.VirtualKey);
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
}
