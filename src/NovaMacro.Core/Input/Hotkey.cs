namespace NovaMacro.Core.Input;

[Flags]
public enum ModifierMask
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// A trigger chord: optional modifier mask (功能键), optional extra held key (副键) and
/// the main key (键). Stored in INI sections named "&lt;功能&gt;热键" as 键/功能键/副键.
/// </summary>
public readonly record struct Hotkey(int Key, ModifierMask Modifiers = ModifierMask.None, int Secondary = InputCodes.None)
{
    public static Hotkey None => new(InputCodes.None);

    public bool IsEmpty => Key == InputCodes.None;

    public static string ModifierText(ModifierMask mask)
    {
        if (mask == ModifierMask.None)
        {
            return "禁用";
        }

        var parts = new List<string>();
        if (mask.HasFlag(ModifierMask.Ctrl))
        {
            parts.Add("Ctrl键");
        }

        if (mask.HasFlag(ModifierMask.Shift))
        {
            parts.Add("Shift键");
        }

        if (mask.HasFlag(ModifierMask.Alt))
        {
            parts.Add("Alt键");
        }

        return string.Concat(parts);
    }

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "未设置";
        }

        var parts = new List<string>();
        if (Modifiers != ModifierMask.None)
        {
            parts.Add(ModifierText(Modifiers).Replace("键", "键 + ").TrimEnd(' ', '+'));
        }

        if (Secondary != InputCodes.None)
        {
            parts.Add(KeyNames.GetName(Secondary));
        }

        parts.Add(KeyNames.GetName(Key));
        return string.Join(" + ", parts);
    }
}
