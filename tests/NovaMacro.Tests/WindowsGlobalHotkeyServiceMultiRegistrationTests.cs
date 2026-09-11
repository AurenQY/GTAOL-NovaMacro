using System.Collections.Concurrent;
using System.ComponentModel;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalHotkeyServiceMultiRegistrationTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    private static readonly KeyboardHotkey HotkeyA = new(HotkeyKey.A);
    private static readonly KeyboardHotkey HotkeyB = new(HotkeyKey.B);
    private static readonly KeyboardHotkey HotkeyC = new(HotkeyKey.C);

    [Fact]
    public async Task RegisterThree_RoutesEachIdOnlyToItsMatchingHotkeyAndIgnoresUnknownId()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;

        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        using GlobalHotkeyRegistration second = service.Register(HotkeyB);
        using GlobalHotkeyRegistration third = service.Register(HotkeyC);

        Assert.Equal(new[] { 1, 2, 3 }, new[]
        {
            first.RegistrationId,
            second.RegistrationId,
            third.RegistrationId
        });
        Assert.Equal(
            new[] { 1, 2, 3 },
            native.RegisterCalls.Select(call => call.RegistrationId));

        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(registrationId: 9999);
        native.EnqueueHotkey(second.RegistrationId);
        native.EnqueueHotkey(third.RegistrationId);
        FenceMessageLoop(service, HotkeyA);
        Assert.Equal(3, scheduler.QueuedCount);
        await AwaitAsync(scheduler.DrainAsync());

        Assert.Equal(new[] { HotkeyA, HotkeyB, HotkeyC }, callbacks);
    }

    [Fact]
    public async Task DisposeMiddleRegistration_RemovesOnlyMiddleAndLeavesOthersActive()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        GlobalHotkeyRegistration middle = service.Register(HotkeyB);
        using GlobalHotkeyRegistration third = service.Register(HotkeyC);

        middle.Dispose();

        Assert.Equal(new[] { middle.RegistrationId }, native.UnregisterCalls);
        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(middle.RegistrationId);
        native.EnqueueHotkey(third.RegistrationId);
        FenceMessageLoop(service, HotkeyA);
        Assert.Equal(2, scheduler.QueuedCount);
        await AwaitAsync(scheduler.DrainAsync());

        Assert.Equal(new[] { HotkeyA, HotkeyC }, callbacks);
    }

    [Fact]
    public void RegistrationHandle_DoubleDisposeIsIdempotent()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        GlobalHotkeyRegistration registration = service.Register(HotkeyA);

        registration.Dispose();
        registration.Dispose();

        Assert.Equal(new[] { registration.RegistrationId }, native.UnregisterCalls);
    }

    [Fact]
    public void RegistrationHandle_DisposeAfterServiceDisposeIsSafeNoOp()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        GlobalHotkeyRegistration registration = service.Register(HotkeyA);

        service.Dispose();
        registration.Dispose();
        registration.Dispose();

        Assert.Equal(new[] { registration.RegistrationId }, native.UnregisterCalls);
    }

    [Fact]
    public async Task RegisterFailure_AffectsOnlyRequestedHotkeyAndDoesNotPoisonService()
    {
        const int expectedNativeErrorCode = 1409;
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        native.FailNextRegister(expectedNativeErrorCode);

        GlobalHotkeyRegistrationException failure =
            Assert.Throws<GlobalHotkeyRegistrationException>(
                () => service.Register(HotkeyB));
        using GlobalHotkeyRegistration third = service.Register(HotkeyC);

        Assert.Equal(HotkeyB, failure.Hotkey);
        Assert.Equal(expectedNativeErrorCode, failure.NativeErrorCode);
        Assert.Equal(
            new[] { 1, 2, 3 },
            native.RegisterCalls.Select(call => call.RegistrationId));

        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(registrationId: 2);
        native.EnqueueHotkey(third.RegistrationId);
        FenceMessageLoop(service, HotkeyA);
        Assert.Equal(2, scheduler.QueuedCount);
        await AwaitAsync(scheduler.DrainAsync());

        Assert.Equal(new[] { HotkeyA, HotkeyC }, callbacks);
    }

    [Fact]
    public async Task DuplicateHotkey_IsRejectedBeforeNativeCallOrIdConsumption()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration original = service.Register(HotkeyA);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => service.Register(new KeyboardHotkey(HotkeyKey.A)));
        using GlobalHotkeyRegistration second = service.Register(HotkeyB);

        Assert.Contains("already registered", failure.Message);
        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));

        native.EnqueueHotkey(original.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        await AwaitAsync(scheduler.DrainAsync());
        Assert.Equal(new[] { HotkeyA }, callbacks);
    }

    [Fact]
    public async Task PendingReleaseWaits_AreIndependentPerRegistration()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        using GlobalHotkeyRegistration second = service.Register(HotkeyB);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyA);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(second.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(2));

        Task firstWait = scheduler.RunNextAsync();
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyA));
        Task secondWait = scheduler.RunNextAsync();
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyB));

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyA);
        await AwaitAsync(firstWait);
        Assert.Equal(new[] { HotkeyA }, callbacks);
        Assert.False(secondWait.IsCompleted);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        await AwaitAsync(secondWait);
        Assert.Equal(new[] { HotkeyA, HotkeyB }, callbacks);
    }

    [Fact]
    public async Task CancelOnePendingRelease_DoesNotCancelOtherPendingRelease()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        GlobalHotkeyRegistration first = service.Register(HotkeyA);
        using GlobalHotkeyRegistration second = service.Register(HotkeyB);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyA);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(second.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(2));

        Task firstWait = scheduler.RunNextAsync();
        Task secondWait = scheduler.RunNextAsync();
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyA));
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyB));

        first.Dispose();
        await AwaitAsync(firstWait);
        Assert.Empty(callbacks);
        Assert.False(secondWait.IsCompleted);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        await AwaitAsync(secondWait);
        Assert.Equal(new[] { HotkeyB }, callbacks);
    }

    [Fact]
    public async Task DisposeRegistrationDuringPendingRelease_CancelsOnlyThatRegistration()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        GlobalHotkeyRegistration middle = service.Register(HotkeyB);
        using GlobalHotkeyRegistration third = service.Register(HotkeyC);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        native.EnqueueHotkey(middle.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        Task pendingRelease = scheduler.RunNextAsync();
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyB));

        middle.Dispose();
        await AwaitAsync(pendingRelease);
        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyB);

        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(middle.RegistrationId);
        native.EnqueueHotkey(third.RegistrationId);
        FenceMessageLoop(service, HotkeyA);
        Assert.Equal(2, scheduler.QueuedCount);
        await AwaitAsync(scheduler.DrainAsync());

        Assert.Equal(new[] { HotkeyA, HotkeyC }, callbacks);
        Assert.Equal(new[] { middle.RegistrationId }, native.UnregisterCalls);
    }

    [Fact]
    public async Task ServiceDispose_CancelsAllPendingReleaseWaitsAndInvalidatesHandles()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        GlobalHotkeyRegistration first = service.Register(HotkeyA);
        GlobalHotkeyRegistration second = service.Register(HotkeyB);
        GlobalHotkeyRegistration third = service.Register(HotkeyC);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyA);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyC);
        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(second.RegistrationId);
        native.EnqueueHotkey(third.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(3));

        Task firstWait = scheduler.RunNextAsync();
        Task secondWait = scheduler.RunNextAsync();
        Task thirdWait = scheduler.RunNextAsync();
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyA));
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyB));
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyC));

        service.Dispose();
        await AwaitAsync(Task.WhenAll(firstWait, secondWait, thirdWait));
        first.Dispose();
        second.Dispose();
        third.Dispose();

        Assert.Empty(callbacks);
        Assert.Equal(new[] { 1, 2, 3 }, native.UnregisterCalls.Order());
    }

    [Fact]
    public void ServiceDispose_WhenMultipleUnregistersFailAttemptsAllAndAggregates()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        GlobalHotkeyRegistration first = service.Register(HotkeyA);
        GlobalHotkeyRegistration second = service.Register(HotkeyB);
        GlobalHotkeyRegistration third = service.Register(HotkeyC);
        native.FailNextUnregister();
        native.FailNextUnregister();

        AggregateException failure = Assert.Throws<AggregateException>(
            service.Dispose);

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.All(
            failure.InnerExceptions,
            exception => Assert.IsType<Win32Exception>(exception));
        Assert.Equal(new[] { 1, 2, 3 }, native.UnregisterCalls.Order());

        first.Dispose();
        second.Dispose();
        third.Dispose();
        Assert.Equal(3, native.UnregisterCalls.Count);
    }

    [Fact]
    public async Task TerminalGetMessageFailure_InvalidatesAllRegistrationsAndCancelsWaits()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        GlobalHotkeyRegistration first = service.Register(HotkeyA);
        GlobalHotkeyRegistration second = service.Register(HotkeyB);
        GlobalHotkeyRegistration third = service.Register(HotkeyC);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyB);
        native.EnqueueHotkey(second.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        Task pendingRelease = scheduler.RunNextAsync();
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyB));

        native.FailNextGetMessage();
        Exception observation = await Task.Run(
                () => CaptureException(service.Unregister))
            .WaitAsync(SafetyTimeout) ?? throw new Xunit.Sdk.XunitException(
                "Expected terminal message-loop failure.");

        Assert.True(
            observation is Win32Exception or
            InvalidOperationException { InnerException: Win32Exception });
        Assert.False(observation is GlobalHotkeyRegistrationException);
        await AwaitAsync(pendingRelease);
        Assert.Empty(callbacks);

        first.Dispose();
        second.Dispose();
        third.Dispose();
        Assert.Empty(native.UnregisterCalls);
        Assert.IsType<Win32Exception>(CaptureException(service.Dispose));
    }

    [Fact]
    public void ReplaceAndUnregister_InvalidateOldHandleWithoutLettingItControlReplacement()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        GlobalHotkeyRegistration original = service.Register(HotkeyA);

        service.Replace(HotkeyB);
        original.Dispose();

        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));

        service.Unregister();
        original.Dispose();

        Assert.Equal(new[] { 1, 2 }, native.UnregisterCalls);
    }

    [Fact]
    public void ReplaceFailure_InvalidatesOldHandleConsumesIdAndDoesNotRollBack()
    {
        const int expectedNativeErrorCode = 1409;
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        GlobalHotkeyRegistration original = service.Register(HotkeyA);
        native.FailNextRegister(expectedNativeErrorCode);

        GlobalHotkeyRegistrationException failure =
            Assert.Throws<GlobalHotkeyRegistrationException>(
                () => service.Replace(HotkeyB));
        original.Dispose();
        using GlobalHotkeyRegistration current = service.Register(HotkeyC);

        Assert.Equal(HotkeyB, failure.Hotkey);
        Assert.Equal(expectedNativeErrorCode, failure.NativeErrorCode);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            new[] { 1, 2, 3 },
            native.RegisterCalls.Select(call => call.RegistrationId));
        Assert.Equal(3, current.RegistrationId);
    }

    [Fact]
    public async Task CompatibilityReplaceAndUnregister_WithMultipleRegistrationsAreRejectedWithoutMutation()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        using GlobalHotkeyRegistration second = service.Register(HotkeyB);

        InvalidOperationException replaceFailure =
            Assert.Throws<InvalidOperationException>(() => service.Replace(HotkeyC));
        InvalidOperationException unregisterFailure =
            Assert.Throws<InvalidOperationException>(service.Unregister);

        Assert.Contains("ambiguous", replaceFailure.Message);
        Assert.Contains("ambiguous", unregisterFailure.Message);
        Assert.Equal(2, native.RegisterCalls.Count);
        Assert.Empty(native.UnregisterCalls);

        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(second.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(2));
        await AwaitAsync(scheduler.DrainAsync());
        Assert.Equal(new[] { HotkeyA, HotkeyB }, callbacks);
    }

    [Fact]
    public async Task ThrowingSubscriber_IsIsolatedAcrossMultipleRegistrations()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += hotkey =>
        {
            if (hotkey == HotkeyA)
            {
                throw new InvalidOperationException("subscriber failure");
            }
        };
        service.Triggered += callbacks.Enqueue;
        using GlobalHotkeyRegistration first = service.Register(HotkeyA);
        using GlobalHotkeyRegistration second = service.Register(HotkeyB);
        using GlobalHotkeyRegistration third = service.Register(HotkeyC);

        native.EnqueueHotkey(first.RegistrationId);
        native.EnqueueHotkey(second.RegistrationId);
        native.EnqueueHotkey(third.RegistrationId);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(3));
        await AwaitAsync(scheduler.DrainAsync());

        Assert.Equal(new[] { HotkeyA, HotkeyB, HotkeyC }, callbacks);
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

    private static void FenceMessageLoop(
        WindowsGlobalHotkeyService service,
        KeyboardHotkey activeHotkey)
    {
        // Duplicate validation runs on the message thread, so its synchronous
        // failure proves that all earlier native messages were processed.
        Assert.Throws<InvalidOperationException>(
            () => service.Register(activeHotkey));
    }

    private static async Task AwaitAsync(Task task)
    {
        await task.WaitAsync(SafetyTimeout);
    }
}
