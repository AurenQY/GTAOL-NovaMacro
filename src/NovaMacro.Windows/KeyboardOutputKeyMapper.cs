using NovaMacro.Core;

namespace NovaMacro.Windows;

internal readonly record struct KeyboardOutputKeyMapping(
    ushort VirtualKey,
    bool IsExtended);

internal static class KeyboardOutputKeyMapper
{
    internal static KeyboardOutputKeyMapping Map(KeyboardKey key)
    {
        return key switch
        {
            KeyboardKey.A => new(0x41, false),
            KeyboardKey.B => new(0x42, false),
            KeyboardKey.C => new(0x43, false),
            KeyboardKey.D => new(0x44, false),
            KeyboardKey.E => new(0x45, false),
            KeyboardKey.F => new(0x46, false),
            KeyboardKey.G => new(0x47, false),
            KeyboardKey.H => new(0x48, false),
            KeyboardKey.I => new(0x49, false),
            KeyboardKey.J => new(0x4A, false),
            KeyboardKey.K => new(0x4B, false),
            KeyboardKey.L => new(0x4C, false),
            KeyboardKey.M => new(0x4D, false),
            KeyboardKey.N => new(0x4E, false),
            KeyboardKey.O => new(0x4F, false),
            KeyboardKey.P => new(0x50, false),
            KeyboardKey.Q => new(0x51, false),
            KeyboardKey.R => new(0x52, false),
            KeyboardKey.S => new(0x53, false),
            KeyboardKey.T => new(0x54, false),
            KeyboardKey.U => new(0x55, false),
            KeyboardKey.V => new(0x56, false),
            KeyboardKey.W => new(0x57, false),
            KeyboardKey.X => new(0x58, false),
            KeyboardKey.Y => new(0x59, false),
            KeyboardKey.Z => new(0x5A, false),
            KeyboardKey.Digit0 => new(0x30, false),
            KeyboardKey.Digit1 => new(0x31, false),
            KeyboardKey.Digit2 => new(0x32, false),
            KeyboardKey.Digit3 => new(0x33, false),
            KeyboardKey.Digit4 => new(0x34, false),
            KeyboardKey.Digit5 => new(0x35, false),
            KeyboardKey.Digit6 => new(0x36, false),
            KeyboardKey.Digit7 => new(0x37, false),
            KeyboardKey.Digit8 => new(0x38, false),
            KeyboardKey.Digit9 => new(0x39, false),
            KeyboardKey.ArrowUp => new(0x26, true),
            KeyboardKey.ArrowDown => new(0x28, true),
            KeyboardKey.ArrowLeft => new(0x25, true),
            KeyboardKey.ArrowRight => new(0x27, true),
            KeyboardKey.Enter => new(0x0D, false),
            KeyboardKey.Escape => new(0x1B, false),
            KeyboardKey.Space => new(0x20, false),
            KeyboardKey.Tab => new(0x09, false),
            KeyboardKey.Backspace => new(0x08, false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "The keyboard key is not supported by the Windows adapter.")
        };
    }
}
