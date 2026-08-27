using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal sealed class ApplicationSettings
{
    internal ApplicationSettings(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        Macro = macro;
    }

    internal MacroDefinition Macro { get; }
}

internal sealed class MacroDefinition
{
    internal MacroDefinition(
        string name,
        KeyboardHotkey hotkey,
        MacroSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(sequence);

        Name = name;
        Hotkey = hotkey;
        Sequence = sequence;
    }

    internal string Name { get; }

    internal KeyboardHotkey Hotkey { get; }

    internal MacroSequence Sequence { get; }
}
