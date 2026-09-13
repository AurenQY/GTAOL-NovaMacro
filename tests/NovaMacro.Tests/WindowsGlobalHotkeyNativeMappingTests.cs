using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class WindowsGlobalHotkeyNativeMappingTests
{
    public static TheoryData<HotkeyKey, uint> KeyMappings => new()
    {
        { HotkeyKey.A, 0x41 },
        { HotkeyKey.B, 0x42 },
        { HotkeyKey.C, 0x43 },
        { HotkeyKey.D, 0x44 },
        { HotkeyKey.E, 0x45 },
        { HotkeyKey.F, 0x46 },
        { HotkeyKey.G, 0x47 },
        { HotkeyKey.H, 0x48 },
        { HotkeyKey.I, 0x49 },
        { HotkeyKey.J, 0x4A },
        { HotkeyKey.K, 0x4B },
        { HotkeyKey.L, 0x4C },
        { HotkeyKey.M, 0x4D },
        { HotkeyKey.N, 0x4E },
        { HotkeyKey.O, 0x4F },
        { HotkeyKey.P, 0x50 },
        { HotkeyKey.Q, 0x51 },
        { HotkeyKey.R, 0x52 },
        { HotkeyKey.S, 0x53 },
        { HotkeyKey.T, 0x54 },
        { HotkeyKey.U, 0x55 },
        { HotkeyKey.V, 0x56 },
        { HotkeyKey.W, 0x57 },
        { HotkeyKey.X, 0x58 },
        { HotkeyKey.Y, 0x59 },
        { HotkeyKey.Z, 0x5A },
        { HotkeyKey.F1, 0x70 },
        { HotkeyKey.F2, 0x71 },
        { HotkeyKey.F3, 0x72 },
        { HotkeyKey.F4, 0x73 },
        { HotkeyKey.F5, 0x74 },
        { HotkeyKey.F6, 0x75 },
        { HotkeyKey.F7, 0x76 },
        { HotkeyKey.F8, 0x77 },
        { HotkeyKey.F9, 0x78 },
        { HotkeyKey.F10, 0x79 },
        { HotkeyKey.F11, 0x7A },
        { HotkeyKey.Numpad0, 0x60 },
        { HotkeyKey.Numpad1, 0x61 },
        { HotkeyKey.Numpad2, 0x62 },
        { HotkeyKey.Numpad3, 0x63 },
        { HotkeyKey.Numpad4, 0x64 },
        { HotkeyKey.Numpad5, 0x65 },
        { HotkeyKey.Numpad6, 0x66 },
        { HotkeyKey.Numpad7, 0x67 },
        { HotkeyKey.Numpad8, 0x68 },
        { HotkeyKey.Numpad9, 0x69 },
        { HotkeyKey.NumpadMultiply, 0x6A },
        { HotkeyKey.NumpadAdd, 0x6B },
        { HotkeyKey.NumpadSubtract, 0x6D },
        { HotkeyKey.NumpadDecimal, 0x6E },
        { HotkeyKey.NumpadDivide, 0x6F }
    };

    public static TheoryData<HotkeyModifiers, uint> ModifierMappings => new()
    {
        { HotkeyModifiers.None, 0x4000 },
        { HotkeyModifiers.Control, 0x4002 },
        { HotkeyModifiers.Alt, 0x4001 },
        { HotkeyModifiers.Shift, 0x4004 },
        { HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4003 },
        { HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4006 },
        { HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x4005 },
        {
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            0x4007
        }
    };

    [Theory]
    [MemberData(nameof(KeyMappings))]
    public void Register_MapsKeyToExpectedVirtualKey(
        HotkeyKey key,
        uint expectedVirtualKey)
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);

        service.Register(new KeyboardHotkey(key));

        FakeGlobalHotkeyNativeApi.RegisterHotkeyCall call =
            Assert.Single(native.RegisterCalls);
        Assert.Equal(expectedVirtualKey, call.VirtualKey);
        Assert.Equal(0x4000u, call.Modifiers);
    }

    [Theory]
    [MemberData(nameof(ModifierMappings))]
    public void Register_MapsModifiersAndAlwaysIncludesNoRepeat(
        HotkeyModifiers modifiers,
        uint expectedNativeMask)
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        using var service = new WindowsGlobalHotkeyService(native);

        service.Register(new KeyboardHotkey(HotkeyKey.F8, modifiers));

        FakeGlobalHotkeyNativeApi.RegisterHotkeyCall call =
            Assert.Single(native.RegisterCalls);
        Assert.Equal(expectedNativeMask, call.Modifiers);
        Assert.NotEqual(0u, call.Modifiers & 0x4000u);
    }

    [Fact]
    public void NumpadTriggerVocabularyUsesNumLockVirtualKeysAndOmitsDistinctEnter()
    {
        string[] names = Enum.GetNames<HotkeyKey>();

        Assert.Contains("Numpad0", names);
        Assert.Contains("NumpadDecimal", names);
        Assert.Contains("NumpadDivide", names);
        Assert.DoesNotContain("NumpadEnter", names);
    }
}
