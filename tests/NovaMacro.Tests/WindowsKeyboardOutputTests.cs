using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsKeyboardOutputTests
{
    [Fact]
    public void KeyDown_UndefinedKeyThrowsBeforeSendInput()
    {
        var keyboard = new WindowsKeyboardOutput();
        var undefinedKey = (KeyboardKey)int.MaxValue;

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => keyboard.KeyDown(undefinedKey));

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void KeyUp_UndefinedKeyThrowsBeforeSendInput()
    {
        var keyboard = new WindowsKeyboardOutput();
        var undefinedKey = (KeyboardKey)int.MaxValue;

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => keyboard.KeyUp(undefinedKey));

        Assert.Equal("key", exception.ParamName);
    }
}
