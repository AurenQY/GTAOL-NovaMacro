using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class KeyboardKeyVocabularyTests
{
    [Fact]
    public void KeyboardKey_ContainsExactlyApprovedV0Vocabulary()
    {
        string[] expectedNames =
        {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6",
            "Digit7", "Digit8", "Digit9",
            "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
            "Enter", "Escape", "Space", "Tab", "Backspace",
            "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4",
            "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
            "NumpadAdd", "NumpadSubtract", "NumpadMultiply",
            "NumpadDivide", "NumpadDecimal", "NumpadEnter"
        };
        string[] actualNames = Enum.GetNames<KeyboardKey>();

        Assert.Equal(61, actualNames.Length);
        Assert.Equal(
            expectedNames.Order(StringComparer.Ordinal),
            actualNames.Order(StringComparer.Ordinal));
    }
}
