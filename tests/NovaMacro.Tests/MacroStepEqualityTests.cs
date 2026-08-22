using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class MacroStepEqualityTests
{
    [Fact]
    public void KeyTapStep_UsesValueEquality()
    {
        var first = new KeyTapStep(KeyboardKey.A);
        var sameValue = new KeyTapStep(KeyboardKey.A);
        var differentValue = new KeyTapStep(KeyboardKey.B);

        Assert.Equal(first, sameValue);
        Assert.NotEqual(first, differentValue);
    }

    [Fact]
    public void DelayStep_UsesValueEquality()
    {
        var first = new DelayStep(TimeSpan.FromMilliseconds(150));
        var sameValue = new DelayStep(TimeSpan.FromMilliseconds(150));
        var differentValue = new DelayStep(TimeSpan.FromMilliseconds(151));

        Assert.Equal(first, sameValue);
        Assert.NotEqual(first, differentValue);
    }
}
