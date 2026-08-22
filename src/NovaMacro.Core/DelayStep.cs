namespace NovaMacro.Core;

public sealed record DelayStep : MacroStep
{
    public DelayStep(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Delay duration cannot be negative.");
        }

        Duration = duration;
    }

    public TimeSpan Duration { get; }
}
