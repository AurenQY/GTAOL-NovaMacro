using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class DelayStepTests
{
    [Fact]
    public void Constructor_RejectsNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DelayStep(TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void Constructor_AcceptsZeroDuration()
    {
        var step = new DelayStep(TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, step.Duration);
    }

    [Fact]
    public void Constructor_PreservesValidDuration()
    {
        TimeSpan duration = TimeSpan.FromMilliseconds(275);

        var step = new DelayStep(duration);

        Assert.Equal(duration, step.Duration);
    }
}
