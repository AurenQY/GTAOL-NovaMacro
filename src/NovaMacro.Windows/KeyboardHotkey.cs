namespace NovaMacro.Windows;

public sealed record KeyboardHotkey
{
    private const HotkeyModifiers SupportedModifiers =
        HotkeyModifiers.Control |
        HotkeyModifiers.Alt |
        HotkeyModifiers.Shift;

    public KeyboardHotkey(
        HotkeyKey key,
        HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "The hotkey trigger key is not supported.");
        }

        if ((modifiers & ~SupportedModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modifiers),
                modifiers,
                "The hotkey contains unsupported modifier flags.");
        }

        Key = key;
        Modifiers = modifiers;
    }

    public HotkeyKey Key { get; }

    public HotkeyModifiers Modifiers { get; }
}
