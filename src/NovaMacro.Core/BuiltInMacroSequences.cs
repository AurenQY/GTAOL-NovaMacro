namespace NovaMacro.Core;

public static class BuiltInMacroSequences
{
    public static MacroSequence AbcTest { get; } = new(
        new MacroStep[]
        {
            new KeyTapStep(KeyboardKey.A),
            new DelayStep(TimeSpan.FromMilliseconds(150)),
            new KeyTapStep(KeyboardKey.B),
            new DelayStep(TimeSpan.FromMilliseconds(150)),
            new KeyTapStep(KeyboardKey.C)
        });
}
