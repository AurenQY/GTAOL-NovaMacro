using NovaMacro.Core;

namespace NovaMacro.App;

internal sealed class TaskDelay : IDelay
{
    public Task WaitAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        return Task.Delay(duration, cancellationToken);
    }
}
