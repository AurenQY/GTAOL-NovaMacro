using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class BuiltInMacroSequencesTests
{
    [Fact]
    public void AbcTest_HasExpectedStepsInOrder()
    {
        IReadOnlyList<MacroStep> steps = BuiltInMacroSequences.AbcTest.Steps;

        Assert.Collection(
            steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.A), step),
            step => Assert.Equal(new DelayStep(TimeSpan.FromMilliseconds(150)), step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.B), step),
            step => Assert.Equal(new DelayStep(TimeSpan.FromMilliseconds(150)), step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.C), step));
    }
}
