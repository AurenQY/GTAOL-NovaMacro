using System.Collections.Concurrent;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalHotkeyServiceCallbackTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task AdmittedCallback_MayFinishAfterUnregisterReturns()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
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
        native.EnqueueHotkey(1);
        await AwaitAsync(subscriberEntered.Task);

        try
        {
            await AwaitAsync(Task.Run(service.Unregister));
            Assert.False(subscriberFinished.Task.IsCompleted);
        }
        finally
        {
            releaseSubscriber.Set();
        }

        await AwaitAsync(subscriberFinished.Task);
    }

    [Fact]
    public async Task LifecycleCutoffBeforeAdmission_PreventsOldCallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        var oldHotkey = new KeyboardHotkey(HotkeyKey.F8);
        service.Register(oldHotkey);
        native.EnqueueHotkey(1);
        FenceMessageLoop(service, oldHotkey);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));

        service.Unregister();
        await AwaitAsync(scheduler.RunNextAsync());
        Assert.Empty(callbacks);

        var currentHotkey = new KeyboardHotkey(HotkeyKey.A);
        service.Register(currentHotkey);
        native.EnqueueHotkey(2);
        FenceMessageLoop(service, currentHotkey);
        await AwaitAsync(scheduler.RunNextAsync());

        Assert.Equal(
            new[] { currentHotkey },
            callbacks);
    }

    [Fact]
    public async Task TriggeredSubscriber_CanUnregisterWithoutDeadlock()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var completed = NewSignal();
        service.Triggered += _ =>
        {
            service.Unregister();
            completed.TrySetResult();
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        native.EnqueueHotkey(1);

        await AwaitAsync(completed.Task);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        service.Register(new KeyboardHotkey(HotkeyKey.A));
    }

    [Fact]
    public async Task TriggeredSubscriber_CanReplaceWithoutDeadlock()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var completed = NewSignal();
        service.Triggered += hotkey =>
        {
            if (hotkey.Key == HotkeyKey.F8)
            {
                service.Replace(new KeyboardHotkey(HotkeyKey.A));
                completed.TrySetResult();
            }
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        native.EnqueueHotkey(1);

        await AwaitAsync(completed.Task);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Equal(
            new[] { 1, 2 },
            native.RegisterCalls.Select(call => call.RegistrationId));
    }

    [Fact]
    public async Task TriggeredSubscriber_CanDisposeWithoutDeadlock()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var service = new WindowsGlobalHotkeyService(native);
        var completed = NewSignal();
        service.Triggered += _ =>
        {
            service.Dispose();
            completed.TrySetResult();
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        native.EnqueueHotkey(1);

        await AwaitAsync(completed.Task);
        Assert.Equal(new[] { 1 }, native.UnregisterCalls);
        Assert.Throws<ObjectDisposedException>(service.Unregister);
    }

    [Fact]
    public async Task PendingReleaseThenUnregister_DoesNotPublishOldCallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        var oldHotkey = new KeyboardHotkey(HotkeyKey.F8);
        service.Register(oldHotkey);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        native.EnqueueHotkey(1);
        FenceMessageLoop(service, oldHotkey);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));

        service.Unregister();
        await AwaitAsync(scheduler.RunNextAsync());
        Assert.Empty(callbacks);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        var currentHotkey = new KeyboardHotkey(HotkeyKey.A);
        service.Register(currentHotkey);
        native.EnqueueHotkey(2);
        FenceMessageLoop(service, currentHotkey);
        await AwaitAsync(scheduler.RunNextAsync());

        Assert.Equal(
            new[] { currentHotkey },
            callbacks);
    }

    [Fact]
    public async Task PendingReleaseThenReplace_DoesNotPublishOldCallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        var oldHotkey = new KeyboardHotkey(HotkeyKey.F8);
        service.Register(oldHotkey);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        native.EnqueueHotkey(1);
        FenceMessageLoop(service, oldHotkey);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));

        var currentHotkey = new KeyboardHotkey(HotkeyKey.A);
        service.Replace(currentHotkey);
        await AwaitAsync(scheduler.RunNextAsync());
        Assert.Empty(callbacks);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        native.EnqueueHotkey(2);
        FenceMessageLoop(service, currentHotkey);
        await AwaitAsync(scheduler.RunNextAsync());

        Assert.Equal(
            new[] { currentHotkey },
            callbacks);
    }

    [Fact]
    public async Task PendingReleaseThenDispose_DoesNotPublishCallback()
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
        FenceMessageLoop(service, hotkey);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));

        service.Dispose();
        await AwaitAsync(scheduler.RunNextAsync());

        Assert.Equal(0, Volatile.Read(ref callbackCount));
    }

    [Fact]
    public async Task ThrowingTriggeredSubscriber_IsIsolatedAndServiceRemainsUsable()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var triggerFailedCount = 0;
        var observedCount = 0;
        var firstCompleted = NewSignal();
        var secondCompleted = NewSignal();
        service.TriggerFailed += (_, _) => Interlocked.Increment(ref triggerFailedCount);
        service.Triggered += _ => throw new InvalidOperationException("subscriber failure");
        service.Triggered += _ =>
        {
            int count = Interlocked.Increment(ref observedCount);
            if (count == 1)
            {
                service.Unregister();
                service.Register(new KeyboardHotkey(HotkeyKey.F8));
                firstCompleted.TrySetResult();
            }
            else
            {
                secondCompleted.TrySetResult();
            }
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));

        native.EnqueueHotkey(1);
        await AwaitAsync(firstCompleted.Task);
        native.EnqueueHotkey(2);
        await AwaitAsync(secondCompleted.Task);

        Assert.Equal(2, observedCount);
        Assert.Equal(0, triggerFailedCount);
    }

    [Fact]
    public async Task ThrowingTriggerFailedSubscriber_IsIsolatedAndLifecycleRemainsUsable()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var failureObserved = NewSignal();
        var laterTrigger = NewSignal();
        service.TriggerFailed += (_, _) => throw new InvalidOperationException(
            "failure subscriber exception");
        service.TriggerFailed += (_, _) =>
        {
            service.Unregister();
            service.Register(new KeyboardHotkey(HotkeyKey.A));
            failureObserved.TrySetResult();
        };
        service.Triggered += hotkey =>
        {
            if (hotkey.Key == HotkeyKey.A)
            {
                laterTrigger.TrySetResult();
            }
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        native.FailNextKeyStateQuery(new InvalidOperationException("key-state failure"));

        native.EnqueueHotkey(1);
        await AwaitAsync(failureObserved.Task);
        native.EnqueueHotkey(2);
        await AwaitAsync(laterTrigger.Task);

        Assert.True(laterTrigger.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ModifierlessTrigger_WaitsUntilTriggerKeyIsReleased()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var triggered = NewSignal();
        var callbackCount = 0;
        service.Triggered += _ =>
        {
            Interlocked.Increment(ref callbackCount);
            triggered.TrySetResult();
        };
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyF8);

        native.EnqueueHotkey(1);
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyF8));
        Assert.False(triggered.Task.IsCompleted);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        await AwaitAsync(triggered.Task);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public async Task ModifiedTrigger_WaitsForEveryConfiguredKeyToBeReleased()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var triggered = NewSignal();
        service.Triggered += _ => triggered.TrySetResult();
        service.Register(new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control | HotkeyModifiers.Shift));
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyControl);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyShift);

        native.EnqueueHotkey(1);
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyF8));
        Assert.False(triggered.Task.IsCompleted);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyControl));
        Assert.False(triggered.Task.IsCompleted);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyControl);
        await AwaitAsync(native.WaitForKeyQueryAsync(
            FakeGlobalHotkeyNativeApi.VirtualKeyShift));
        Assert.False(triggered.Task.IsCompleted);

        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyShift);
        await AwaitAsync(triggered.Task);
    }

    [Fact]
    public async Task UnconfiguredModifier_DoesNotBlockReleaseCompletion()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var triggered = NewSignal();
        service.Triggered += _ => triggered.TrySetResult();
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyControl);

        native.EnqueueHotkey(1);

        await AwaitAsync(triggered.Task);
    }

    [Fact]
    public async Task ReleaseTimeout_RaisesTimeoutFailureOnceWithoutTriggered()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failureCount = 0;
        var triggerCount = 0;
        service.TriggerFailed += (_, exception) =>
        {
            Interlocked.Increment(ref failureCount);
            failure.TrySetResult(exception);
        };
        service.Triggered += _ => Interlocked.Increment(ref triggerCount);
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        native.SetKeyDown(FakeGlobalHotkeyNativeApi.VirtualKeyF8);

        native.EnqueueHotkey(1);

        Exception exception = await failure.Task.WaitAsync(SafetyTimeout);
        native.SetKeyUp(FakeGlobalHotkeyNativeApi.VirtualKeyF8);
        Assert.IsType<TimeoutException>(exception);
        Assert.Equal(1, failureCount);
        Assert.Equal(0, triggerCount);
    }

    [Fact]
    public async Task RapidAtoBtoA_StaleFirstRegistrationMessageDoesNotAddCallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        var firstA = new KeyboardHotkey(HotkeyKey.A);
        var currentA = new KeyboardHotkey(HotkeyKey.A);
        service.Register(firstA);
        service.Replace(new KeyboardHotkey(HotkeyKey.B));
        service.Replace(currentA);
        FenceMessageLoop(service, currentA);

        native.EnqueueHotkey(1);
        FenceMessageLoop(service, currentA);
        Assert.Equal(0, scheduler.QueuedCount);

        native.EnqueueHotkey(3);
        FenceMessageLoop(service, currentA);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        Assert.Equal(1, scheduler.QueuedCount);
        await AwaitAsync(scheduler.RunNextAsync());

        Assert.Equal(
            new[] { currentA },
            callbacks);
    }

    [Fact]
    public async Task UnregisterThenRegister_StaleOldMessageDoesNotAddCallback()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        using var service = new WindowsGlobalHotkeyService(native, scheduler);
        var callbacks = new ConcurrentQueue<KeyboardHotkey>();
        service.Triggered += callbacks.Enqueue;
        service.Register(new KeyboardHotkey(HotkeyKey.F8));
        service.Unregister();
        var currentHotkey = new KeyboardHotkey(HotkeyKey.A);
        service.Register(currentHotkey);
        FenceMessageLoop(service, currentHotkey);

        native.EnqueueHotkey(1);
        FenceMessageLoop(service, currentHotkey);
        Assert.Equal(0, scheduler.QueuedCount);

        native.EnqueueHotkey(2);
        FenceMessageLoop(service, currentHotkey);
        await AwaitAsync(scheduler.WaitForQueuedCountAsync(1));
        Assert.Equal(1, scheduler.QueuedCount);
        await AwaitAsync(scheduler.RunNextAsync());

        Assert.Equal(
            new[] { currentHotkey },
            callbacks);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static void FenceMessageLoop(
        WindowsGlobalHotkeyService service,
        KeyboardHotkey currentHotkey)
    {
        service.Replace(new KeyboardHotkey(
            currentHotkey.Key,
            currentHotkey.Modifiers));
    }

    private static async Task AwaitAsync(Task task)
    {
        await task.WaitAsync(SafetyTimeout);
    }
}
