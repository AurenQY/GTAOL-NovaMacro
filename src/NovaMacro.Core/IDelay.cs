namespace NovaMacro.Core;

public interface IDelay
{
    Task WaitAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default);
}
