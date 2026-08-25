using System.ComponentModel;
using System.Runtime.InteropServices;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsKeyboardOutputTests
{
    private const int TestNativeErrorCode = 12345;

    public static TheoryData<KeyboardKey, ushort, bool> SupportedKeys => new()
    {
        { KeyboardKey.A, 0x41, false },
        { KeyboardKey.B, 0x42, false },
        { KeyboardKey.C, 0x43, false },
        { KeyboardKey.D, 0x44, false },
        { KeyboardKey.E, 0x45, false },
        { KeyboardKey.F, 0x46, false },
        { KeyboardKey.G, 0x47, false },
        { KeyboardKey.H, 0x48, false },
        { KeyboardKey.I, 0x49, false },
        { KeyboardKey.J, 0x4A, false },
        { KeyboardKey.K, 0x4B, false },
        { KeyboardKey.L, 0x4C, false },
        { KeyboardKey.M, 0x4D, false },
        { KeyboardKey.N, 0x4E, false },
        { KeyboardKey.O, 0x4F, false },
        { KeyboardKey.P, 0x50, false },
        { KeyboardKey.Q, 0x51, false },
        { KeyboardKey.R, 0x52, false },
        { KeyboardKey.S, 0x53, false },
        { KeyboardKey.T, 0x54, false },
        { KeyboardKey.U, 0x55, false },
        { KeyboardKey.V, 0x56, false },
        { KeyboardKey.W, 0x57, false },
        { KeyboardKey.X, 0x58, false },
        { KeyboardKey.Y, 0x59, false },
        { KeyboardKey.Z, 0x5A, false },
        { KeyboardKey.Digit0, 0x30, false },
        { KeyboardKey.Digit1, 0x31, false },
        { KeyboardKey.Digit2, 0x32, false },
        { KeyboardKey.Digit3, 0x33, false },
        { KeyboardKey.Digit4, 0x34, false },
        { KeyboardKey.Digit5, 0x35, false },
        { KeyboardKey.Digit6, 0x36, false },
        { KeyboardKey.Digit7, 0x37, false },
        { KeyboardKey.Digit8, 0x38, false },
        { KeyboardKey.Digit9, 0x39, false },
        { KeyboardKey.ArrowUp, 0x26, true },
        { KeyboardKey.ArrowDown, 0x28, true },
        { KeyboardKey.ArrowLeft, 0x25, true },
        { KeyboardKey.ArrowRight, 0x27, true },
        { KeyboardKey.Enter, 0x0D, false },
        { KeyboardKey.Escape, 0x1B, false },
        { KeyboardKey.Space, 0x20, false },
        { KeyboardKey.Tab, 0x09, false },
        { KeyboardKey.Backspace, 0x08, false }
    };

    [Theory]
    [MemberData(nameof(SupportedKeys))]
    public void KeyDown_SubmitsExactSingleInputPacket(
        KeyboardKey key,
        ushort expectedVirtualKey,
        bool isExtended)
    {
        AssertPacket(
            key,
            expectedVirtualKey,
            expectedFlags: isExtended ? 0x0001u : 0u,
            isKeyUp: false);
    }

    [Theory]
    [MemberData(nameof(SupportedKeys))]
    public void KeyUp_SubmitsExactSingleInputPacket(
        KeyboardKey key,
        ushort expectedVirtualKey,
        bool isExtended)
    {
        AssertPacket(
            key,
            expectedVirtualKey,
            expectedFlags: isExtended ? 0x0003u : 0x0002u,
            isKeyUp: true);
    }

    [Fact]
    public void ExtendedKeyFlag_IsUsedOnlyForFourArrowKeys()
    {
        KeyboardKey[] arrows =
        {
            KeyboardKey.ArrowUp,
            KeyboardKey.ArrowDown,
            KeyboardKey.ArrowLeft,
            KeyboardKey.ArrowRight
        };

        foreach (object[] row in SupportedKeys)
        {
            var key = (KeyboardKey)row[0];
            bool shouldBeExtended = arrows.Contains(key);
            var native = new FakeKeyboardInputNativeApi();
            var keyboard = new WindowsKeyboardOutput(native);

            keyboard.KeyDown(key);
            keyboard.KeyUp(key);

            Assert.Collection(
                native.Calls,
                down => Assert.Equal(
                    shouldBeExtended ? 0x0001u : 0u,
                    Assert.Single(down.Inputs).Data.Keyboard.Flags),
                up => Assert.Equal(
                    shouldBeExtended ? 0x0003u : 0x0002u,
                    Assert.Single(up.Inputs).Data.Keyboard.Flags));
        }
    }

    [Fact]
    public void KeyDown_UndefinedKeyThrowsBeforeNativeCall()
    {
        var native = new FakeKeyboardInputNativeApi();
        var keyboard = new WindowsKeyboardOutput(native);
        var undefinedKey = (KeyboardKey)int.MaxValue;

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => keyboard.KeyDown(undefinedKey));

        Assert.Equal("key", exception.ParamName);
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void KeyUp_UndefinedKeyThrowsBeforeNativeCall()
    {
        var native = new FakeKeyboardInputNativeApi();
        var keyboard = new WindowsKeyboardOutput(native);
        var undefinedKey = (KeyboardKey)int.MaxValue;

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => keyboard.KeyUp(undefinedKey));

        Assert.Equal("key", exception.ParamName);
        Assert.Empty(native.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFailure_ThrowsWin32ExceptionWithoutRetry(bool isKeyUp)
    {
        var native = new FakeKeyboardInputNativeApi
        {
            Result = new KeyboardInputNativeResult(
                InsertedCount: 0,
                ErrorCode: TestNativeErrorCode)
        };
        var keyboard = new WindowsKeyboardOutput(native);
        KeyboardKey key = isKeyUp ? KeyboardKey.ArrowUp : KeyboardKey.M;

        Win32Exception exception = Assert.Throws<Win32Exception>(() =>
        {
            if (isKeyUp)
            {
                keyboard.KeyUp(key);
            }
            else
            {
                keyboard.KeyDown(key);
            }
        });

        Assert.Equal(TestNativeErrorCode, exception.NativeErrorCode);
        Assert.Contains(isKeyUp ? "key-up" : "key-down", exception.Message);
        Assert.Contains($"'{key}'", exception.Message);
        Assert.Single(native.Calls);
    }

    [Theory]
    [InlineData(KeyboardKey.A, 0x41)]
    [InlineData(KeyboardKey.B, 0x42)]
    [InlineData(KeyboardKey.C, 0x43)]
    public void AbcRegression_PreservesOrdinaryVirtualKeysAndFlags(
        KeyboardKey key,
        ushort expectedVirtualKey)
    {
        var native = new FakeKeyboardInputNativeApi();
        var keyboard = new WindowsKeyboardOutput(native);

        keyboard.KeyDown(key);
        keyboard.KeyUp(key);

        Assert.Collection(
            native.Calls,
            call => AssertKeyboardFields(
                Assert.Single(call.Inputs),
                expectedVirtualKey,
                expectedFlags: 0),
            call => AssertKeyboardFields(
                Assert.Single(call.Inputs),
                expectedVirtualKey,
                expectedFlags: 0x0002));
    }

    private static void AssertPacket(
        KeyboardKey key,
        ushort expectedVirtualKey,
        uint expectedFlags,
        bool isKeyUp)
    {
        var native = new FakeKeyboardInputNativeApi();
        var keyboard = new WindowsKeyboardOutput(native);

        if (isKeyUp)
        {
            keyboard.KeyUp(key);
        }
        else
        {
            keyboard.KeyDown(key);
        }

        FakeKeyboardInputNativeApi.SendInputCall call =
            Assert.Single(native.Calls);
        Assert.Equal(Marshal.SizeOf<NativeMethods.Input>(), call.InputSize);
        AssertKeyboardFields(
            Assert.Single(call.Inputs),
            expectedVirtualKey,
            expectedFlags);
    }

    private static void AssertKeyboardFields(
        NativeMethods.Input input,
        ushort expectedVirtualKey,
        uint expectedFlags)
    {
        Assert.Equal(NativeMethods.InputKeyboard, input.Type);
        Assert.Equal(expectedVirtualKey, input.Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)0, input.Data.Keyboard.ScanCode);
        Assert.Equal(expectedFlags, input.Data.Keyboard.Flags);
        Assert.Equal(0u, input.Data.Keyboard.Time);
        Assert.Equal((nuint)0, input.Data.Keyboard.ExtraInfo);
    }
}
