using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal sealed class FakeHotkeyTriggerWorkScheduler
    : IHotkeyTriggerWorkScheduler
{
    private readonly object _gate = new();
    private readonly Queue<Func<Task>> _queuedWork = new();
    private readonly List<CountWaiter> _countWaiters = new();

    internal int QueuedCount
    {
        get
        {
            lock (_gate)
            {
                return _queuedWork.Count;
            }
        }
    }

    public void Queue(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        List<TaskCompletionSource>? completedWaiters = null;

        lock (_gate)
        {
            _queuedWork.Enqueue(work);
            int count = _queuedWork.Count;

            for (int index = _countWaiters.Count - 1; index >= 0; index--)
            {
                CountWaiter waiter = _countWaiters[index];
                if (count >= waiter.ExpectedCount)
                {
                    completedWaiters ??= new List<TaskCompletionSource>();
                    completedWaiters.Add(waiter.Completion);
                    _countWaiters.RemoveAt(index);
                }
            }
        }

        if (completedWaiters is not null)
        {
            foreach (TaskCompletionSource completion in completedWaiters)
            {
                completion.TrySetResult();
            }
        }
    }

    internal Task WaitForQueuedCountAsync(int expectedCount)
    {
        if (expectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        }

        lock (_gate)
        {
            if (_queuedWork.Count >= expectedCount)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _countWaiters.Add(new CountWaiter(expectedCount, completion));
            return completion.Task;
        }
    }

    internal async Task RunNextAsync()
    {
        Func<Task> work;

        lock (_gate)
        {
            if (!_queuedWork.TryDequeue(out work!))
            {
                throw new InvalidOperationException("No trigger work is queued.");
            }
        }

        await work().ConfigureAwait(false);
    }

    internal async Task DrainAsync()
    {
        while (true)
        {
            Func<Task>? work;

            lock (_gate)
            {
                if (!_queuedWork.TryDequeue(out work))
                {
                    return;
                }
            }

            await work().ConfigureAwait(false);
        }
    }

    private sealed record CountWaiter(
        int ExpectedCount,
        TaskCompletionSource Completion);
}
