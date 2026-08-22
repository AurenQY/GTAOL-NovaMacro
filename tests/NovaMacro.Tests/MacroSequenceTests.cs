using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class MacroSequenceTests
{
    [Fact]
    public void Constructor_DefensivelyCopiesInputSequence()
    {
        var originalSteps = new List<MacroStep>
        {
            new KeyTapStep(KeyboardKey.A)
        };
        var sequence = new MacroSequence(originalSteps);

        originalSteps[0] = new KeyTapStep(KeyboardKey.B);
        originalSteps.Add(new KeyTapStep(KeyboardKey.C));

        MacroStep step = Assert.Single(sequence.Steps);
        Assert.Equal(new KeyTapStep(KeyboardKey.A), step);
    }

    [Fact]
    public void Steps_CannotBeModified()
    {
        var sequence = new MacroSequence(
            new MacroStep[] { new KeyTapStep(KeyboardKey.A) });
        IReadOnlyList<MacroStep> exposedSteps = sequence.Steps;
        ICollection<MacroStep>? collectionView = exposedSteps as ICollection<MacroStep>;

        Assert.False(collectionView is { IsReadOnly: false });
        if (collectionView is not null)
        {
            Assert.Throws<NotSupportedException>(
                () => collectionView.Add(new KeyTapStep(KeyboardKey.B)));
        }

        Assert.Equal(new KeyTapStep(KeyboardKey.A), Assert.Single(sequence.Steps));
    }

    [Fact]
    public void Constructor_RejectsNullEnumerable()
    {
        Assert.Throws<ArgumentNullException>(() => new MacroSequence(null!));
    }

    [Fact]
    public void Constructor_RejectsNullElements()
    {
        var steps = new MacroStep[]
        {
            new KeyTapStep(KeyboardKey.A),
            null!
        };

        Assert.Throws<ArgumentException>(() => new MacroSequence(steps));
    }
}
