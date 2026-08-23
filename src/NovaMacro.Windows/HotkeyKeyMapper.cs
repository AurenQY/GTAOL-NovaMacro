namespace NovaMacro.Windows;

internal static class HotkeyKeyMapper
{
    internal static uint ToVirtualKey(HotkeyKey key)
    {
        return key switch
        {
            HotkeyKey.A => 0x41,
            HotkeyKey.B => 0x42,
            HotkeyKey.C => 0x43,
            HotkeyKey.D => 0x44,
            HotkeyKey.E => 0x45,
            HotkeyKey.F => 0x46,
            HotkeyKey.G => 0x47,
            HotkeyKey.H => 0x48,
            HotkeyKey.I => 0x49,
            HotkeyKey.J => 0x4A,
            HotkeyKey.K => 0x4B,
            HotkeyKey.L => 0x4C,
            HotkeyKey.M => 0x4D,
            HotkeyKey.N => 0x4E,
            HotkeyKey.O => 0x4F,
            HotkeyKey.P => 0x50,
            HotkeyKey.Q => 0x51,
            HotkeyKey.R => 0x52,
            HotkeyKey.S => 0x53,
            HotkeyKey.T => 0x54,
            HotkeyKey.U => 0x55,
            HotkeyKey.V => 0x56,
            HotkeyKey.W => 0x57,
            HotkeyKey.X => 0x58,
            HotkeyKey.Y => 0x59,
            HotkeyKey.Z => 0x5A,
            HotkeyKey.F1 => 0x70,
            HotkeyKey.F2 => 0x71,
            HotkeyKey.F3 => 0x72,
            HotkeyKey.F4 => 0x73,
            HotkeyKey.F5 => 0x74,
            HotkeyKey.F6 => 0x75,
            HotkeyKey.F7 => 0x76,
            HotkeyKey.F8 => 0x77,
            HotkeyKey.F9 => 0x78,
            HotkeyKey.F10 => 0x79,
            HotkeyKey.F11 => 0x7A,
            _ => throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "The hotkey trigger key is not supported by Windows.")
        };
    }
}
