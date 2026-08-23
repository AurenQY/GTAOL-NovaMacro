using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class KeyboardHotkeyTests
{
    public static TheoryData<HotkeyKey> LetterKeys => new()
    {
        HotkeyKey.A,
        HotkeyKey.B,
        HotkeyKey.C,
        HotkeyKey.D,
        HotkeyKey.E,
        HotkeyKey.F,
        HotkeyKey.G,
        HotkeyKey.H,
        HotkeyKey.I,
        HotkeyKey.J,
        HotkeyKey.K,
        HotkeyKey.L,
        HotkeyKey.M,
        HotkeyKey.N,
        HotkeyKey.O,
        HotkeyKey.P,
        HotkeyKey.Q,
        HotkeyKey.R,
        HotkeyKey.S,
        HotkeyKey.T,
        HotkeyKey.U,
        HotkeyKey.V,
        HotkeyKey.W,
        HotkeyKey.X,
        HotkeyKey.Y,
        HotkeyKey.Z
    };

    public static TheoryData<HotkeyKey> FunctionKeys => new()
    {
        HotkeyKey.F1,
        HotkeyKey.F2,
        HotkeyKey.F3,
        HotkeyKey.F4,
        HotkeyKey.F5,
        HotkeyKey.F6,
        HotkeyKey.F7,
        HotkeyKey.F8,
        HotkeyKey.F9,
        HotkeyKey.F10,
        HotkeyKey.F11
    };

    public static TheoryData<HotkeyModifiers> SupportedModifiers => new()
    {
        HotkeyModifiers.Control,
        HotkeyModifiers.Alt,
        HotkeyModifiers.Shift,
        HotkeyModifiers.Control | HotkeyModifiers.Alt,
        HotkeyModifiers.Control | HotkeyModifiers.Shift,
        HotkeyModifiers.Alt | HotkeyModifiers.Shift,
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift
    };

    [Theory]
    [MemberData(nameof(LetterKeys))]
    public void Constructor_AcceptsLetterKeys(HotkeyKey key)
    {
        var hotkey = new KeyboardHotkey(key);

        Assert.Equal(key, hotkey.Key);
    }

    [Theory]
    [MemberData(nameof(FunctionKeys))]
    public void Constructor_AcceptsF1ThroughF11(HotkeyKey key)
    {
        var hotkey = new KeyboardHotkey(key);

        Assert.Equal(key, hotkey.Key);
    }

    [Fact]
    public void PublicVocabulary_DoesNotContainF12()
    {
        Assert.False(Enum.TryParse("F12", out HotkeyKey _));
    }

    [Theory]
    [InlineData(HotkeyKey.A)]
    [InlineData(HotkeyKey.F8)]
    public void Constructor_AcceptsModifierlessHotkeys(HotkeyKey key)
    {
        var hotkey = new KeyboardHotkey(key);

        Assert.Equal(HotkeyModifiers.None, hotkey.Modifiers);
    }

    [Theory]
    [MemberData(nameof(SupportedModifiers))]
    public void Constructor_AcceptsSupportedModifierCombinations(
        HotkeyModifiers modifiers)
    {
        var hotkey = new KeyboardHotkey(HotkeyKey.F8, modifiers);

        Assert.Equal(modifiers, hotkey.Modifiers);
    }

    [Fact]
    public void Constructor_RejectsUndefinedKey()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new KeyboardHotkey((HotkeyKey)int.MaxValue));

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsUnknownModifierBits()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new KeyboardHotkey(HotkeyKey.F8, (HotkeyModifiers)8));

        Assert.Equal("modifiers", exception.ParamName);
    }

    [Fact]
    public void Equality_UsesKeyAndModifierValues()
    {
        var first = new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control | HotkeyModifiers.Shift);
        var sameValue = new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control | HotkeyModifiers.Shift);
        var differentKey = new KeyboardHotkey(
            HotkeyKey.F9,
            HotkeyModifiers.Control | HotkeyModifiers.Shift);
        var differentModifiers = new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control);

        Assert.Equal(first, sameValue);
        Assert.NotEqual(first, differentKey);
        Assert.NotEqual(first, differentModifiers);
    }

    [Fact]
    public void PublicState_RemainsStableAfterConstruction()
    {
        var hotkey = new KeyboardHotkey(
            HotkeyKey.F8,
            HotkeyModifiers.Control | HotkeyModifiers.Alt);
        KeyboardHotkey copy = hotkey with { };

        Assert.Equal(HotkeyKey.F8, hotkey.Key);
        Assert.Equal(
            HotkeyModifiers.Control | HotkeyModifiers.Alt,
            hotkey.Modifiers);
        Assert.Equal(hotkey, copy);
    }
}
