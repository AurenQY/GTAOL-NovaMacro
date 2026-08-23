namespace NovaMacro.Windows;

internal sealed class ThreadPoolHotkeyTriggerWorkScheduler
    : IHotkeyTriggerWorkScheduler
{
    public void Queue(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = Task.Run(work);
    }
}
