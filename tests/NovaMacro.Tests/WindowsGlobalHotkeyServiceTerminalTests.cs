using System.ComponentModel;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalHotkeyServiceTerminalTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task UnexpectedQuit_ClosesAdmissionAndCancelsPendingRelease()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbackCount = 0;
        service.Triggered += _ => Interlocked.Increment(ref callbackCount);
        var hotkey = new KeyboardHotkey(HotkeyKey.F8);
        service.Register(hotkey);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        native.EnqueueHotkey(1);
        service.Replace(new KeyboardHotkey(hotkey.Key, hotkey.Modifiers));
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));

        native.EnqueueQuit();
        Exception terminalObservation = await Task.Run(
                () => CaptureException(service.Unregister))
            .WaitAsync(SafetyTimeout) ?? throw new Xunit.Sdk.XunitException(
                "Expected terminal failure.");

        Assert.IsType<InvalidOperationException>(terminalObservation);
        InvalidOperationException laterFailure = Assert.Throws<InvalidOperationException>(
            () => service.Register(new KeyboardHotkey(HotkeyKey.A)));
        Assert.Contains("terminated", laterFailure.Message);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        await AwaitAsync(scheduler.DrainAsync());
        Assert.Equal(0, Volatile.Read(ref callbackCount));
        Assert.IsType<InvalidOperationException>(CaptureException(service.Dispose));
    }

    [Fact]
    public void DisposeRequestedQuit_IsNormalShutdown()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        service.Dispose();
        service.Dispose();

        Assert.Equal(
            1,
            native.PostCalls.Count(call =>
                call.Message == FakeGlobalHotkeyNativeApi.WmQuit));
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
    }

    [Fact]
    public async Task GetMessageFailure_FailsQueuedCommandWithoutHanging()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        native.PauseMessageProcessing();
        native.FailNextGetMessage();
        Task<Exception?> register = Task.Run(
            () => CaptureException(
                () => service.Register(new KeyboardHotkey(HotkeyKey.F8))));
        await AwaitAsync(native.WaitForPostAsync());

        native.ResumeMessageProcessing();
        Exception failure = await register.WaitAsync(SafetyTimeout)
            ?? throw new Xunit.Sdk.XunitException("Expected GetMessage failure.");

        Assert.IsType<Win32Exception>(failure);
        InvalidOperationException laterFailure = Assert.Throws<InvalidOperationException>(
            service.Unregister);
        Assert.Contains("terminated", laterFailure.Message);
        Assert.IsType<Win32Exception>(laterFailure.InnerException);
        Assert.IsType<Win32Exception>(CaptureException(service.Dispose));
    }

    [Fact]
    public async Task TerminalFailure_DoesNotWaitForAlreadyAdmittedBlockedSubscriber()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        using var releaseSubscriber = new ManualResetEventSlim(false);
        var subscriberEntered = NewSignal();
        var subscriberFinished = NewSignal();
        service.Triggered += _ =>
        {
            subscriberEntered.TrySetResult();
            releaseSubscriber.Wait();
            subscriberFinished.TrySetResult();
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        await AwaitAsync(native.WaitForPostAsync());
        native.EnqueueHotkey(1);
        await AwaitAsync(subscriberEntered.Task);
        native.PauseMessageProcessing();
        native.FailNextGetMessage();
        Task<Exception?> unregister = Task.Run(
            () => CaptureException(service.Unregister));
        await AwaitAsync(native.WaitForPostAsync());

        try
        {
            native.ResumeMessageProcessing();
            Exception failure = await unregister.WaitAsync(SafetyTimeout)
                ?? throw new Xunit.Sdk.XunitException(
                    "Expected queued lifecycle command failure.");
            Assert.IsType<Win32Exception>(failure);
            Assert.False(subscriberFinished.Task.IsCompleted);
        }
        finally
        {
            releaseSubscriber.Set();
        }

        await AwaitAsync(subscriberFinished.Task);
        Assert.IsType<Win32Exception>(CaptureException(service.Dispose));
    }

    [Fact]
    public async Task DisposeAfterTerminalFailure_ReusesStoredFailureWithoutSecondCleanup()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        native.FailNextGetMessage();

        Exception observation = await Task.Run(
                () => CaptureException(service.Unregister))
            .WaitAsync(SafetyTimeout) ?? throw new Xunit.Sdk.XunitException(
                "Expected terminal failure.");
        Assert.True(
            observation is Win32Exception or
            InvalidOperationException { InnerException: Win32Exception });

        Exception firstDispose = CaptureException(service.Dispose)
            ?? throw new Xunit.Sdk.XunitException("Expected stored failure.");
        Exception secondDispose = CaptureException(service.Dispose)
            ?? throw new Xunit.Sdk.XunitException("Expected stored failure.");

        Assert.IsType<Win32Exception>(firstDispose);
        Assert.IsType<Win32Exception>(secondDispose);
        Assert.Contains("message loop failed", firstDispose.Message);
        Assert.Equal(firstDispose.Message, secondDispose.Message);
        Assert.Empty(native.UnregisterCalls);
        Assert.DoesNotContain(
            native.PostCalls,
            call => call.Message == FakeGlobalHotkeyNativeApi.WmQuit);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(
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

    private static async Task AwaitAsync(Task task)
    {
        await task.WaitAsync(SafetyTimeout);
    }
}
