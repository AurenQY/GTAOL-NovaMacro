namespace NovaMacro.Windows;

internal interface IHotkeyTriggerWorkScheduler
{
    void Queue(Func<Task> work);
}
