using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalHotkeyServiceConcurrencyTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task ConcurrentDisposeCallers_PerformOneSuccessfulCleanup()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.PauseMessageProcessing();

        Task first = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());
        Task second = Task.Run(service.Dispose);
        Task third = Task.Run(service.Dispose);
        Task fourth = Task.Run(service.Dispose);

        native.ResumeMessageProcessing();
        await AwaitAsync(Task.WhenAll(first, second, third, fourth));

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            1,
            native.PostCalls.Count(call =>
                call.Message == FakeGlobalHotkeyNativeApi.WmQuit));
    }

    [Fact]
    public async Task ConcurrentDisposeCallers_ObserveSameCleanupFailure()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.FailNextUnregister();
        native.PauseMessageProcessing();

        Task<Exception?> first = Task.Run(() => CaptureException(service.Dispose));
        await AwaitAsync(native.WaitForPostAsync());
        Task<Exception?> second = Task.Run(() => CaptureException(service.Dispose));
        Task<Exception?> third = Task.Run(() => CaptureException(service.Dispose));

        native.ResumeMessageProcessing();
        Exception?[] failures = await Task.WhenAll(first, second, third)
            .WaitAsync(SafetyTimeout);

        Exception expected = Assert.IsType<System.ComponentModel.Win32Exception>(
            failures[0]);
        Assert.Contains("Failed to unregister", expected.Message);
        foreach (Exception? failure in failures.Skip(1))
        {
            Exception actual = Assert.IsType<System.ComponentModel.Win32Exception>(
                failure);
            Assert.Equal(expected.Message, actual.Message);
        }
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            1,
            native.PostCalls.Count(call =>
                call.Message == FakeGlobalHotkeyNativeApi.WmQuit));
    }

    [Fact]
    public async Task RegisterRacingWithDispose_AllowsOnlyOrderedSuccessOrDisposedRejection()
    {
        await VerifyRegisterAdmittedBeforeDisposeAsync();
        await VerifyRegisterRejectedAfterDisposeCutoffAsync();
    }

    [Fact]
    public async Task ReplaceRacingWithDispose_AllowsOnlyOrderedSuccessOrDisposedRejection()
    {
        await VerifyReplaceAdmittedBeforeDisposeAsync();
        await VerifyReplaceRejectedAfterDisposeCutoffAsync();
    }

    [Fact]
    public async Task UnregisterRacingWithDispose_AllowsOnlyOrderedSuccessOrDisposedRejection()
    {
        await VerifyUnregisterAdmittedBeforeDisposeAsync();
        await VerifyUnregisterRejectedAfterDisposeCutoffAsync();
    }

    private static async Task VerifyRegisterAdmittedBeforeDisposeAsync()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        native.PauseMessageProcessing();

        Task register = Task.Run(
            () => service.Register(new KeyboardHotkey(HotkeyKey.F8)));
        await AwaitAsync(native.WaitForPostAsync());
        Task dispose = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());

        native.ResumeMessageProcessing();
        await AwaitAsync(Task.WhenAll(register, dispose));

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
    }

    private static async Task VerifyRegisterRejectedAfterDisposeCutoffAsync()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        native.PauseMessageProcessing();

        Task dispose = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());
        Task<Exception?> register = Task.Run(
            () => CaptureException(
                () => service.Register(new KeyboardHotkey(HotkeyKey.F8))));

        Assert.IsType<ObjectDisposedException>(await register.WaitAsync(SafetyTimeout));
        native.ResumeMessageProcessing();
        await AwaitAsync(dispose);
    }

    private static async Task VerifyReplaceAdmittedBeforeDisposeAsync()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.PauseMessageProcessing();

        Task replace = Task.Run(
            () => service.Replace(new KeyboardHotkey(HotkeyKey.A)));
        await AwaitAsync(native.WaitForPostAsync());
        Task dispose = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());

        native.ResumeMessageProcessing();
        await AwaitAsync(Task.WhenAll(replace, dispose));

        Assert.Equal(new[] { 1, 2 }, native.UnregisterCalls);
    }

    private static async Task VerifyReplaceRejectedAfterDisposeCutoffAsync()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.PauseMessageProcessing();

        Task dispose = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());
        Task<Exception?> replace = Task.Run(
            () => CaptureException(
                () => service.Replace(new KeyboardHotkey(HotkeyKey.A))));

        Assert.IsType<ObjectDisposedException>(await replace.WaitAsync(SafetyTimeout));
        native.ResumeMessageProcessing();
        await AwaitAsync(dispose);
    }

    private static async Task VerifyUnregisterAdmittedBeforeDisposeAsync()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.PauseMessageProcessing();

        Task unregister = Task.Run(service.Unregister);
        await AwaitAsync(native.WaitForPostAsync());
        Task dispose = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());

        native.ResumeMessageProcessing();
        await AwaitAsync(Task.WhenAll(unregister, dispose));

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
    }

    private static async Task VerifyUnregisterRejectedAfterDisposeCutoffAsync()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.PauseMessageProcessing();

        Task dispose = Task.Run(service.Dispose);
        await AwaitAsync(native.WaitForPostAsync());
        Task<Exception?> unregister = Task.Run(
            () => CaptureException(service.Unregister));

        Assert.IsType<ObjectDisposedException>(
            await unregister.WaitAsync(SafetyTimeout));
        native.ResumeMessageProcessing();
        await AwaitAsync(dispose);
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

    private static async Task AwaitAsync(Task task)
    {
        await task.WaitAsync(SafetyTimeout);
    }
}
