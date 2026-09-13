using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal abstract record MacroTrigger;

internal sealed record KeyboardMacroTrigger : MacroTrigger
{
    internal KeyboardMacroTrigger(KeyboardHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        Hotkey = hotkey;
    }

    internal KeyboardHotkey Hotkey { get; }
}

internal sealed record MouseButtonMacroTrigger : MacroTrigger
{
    internal MouseButtonMacroTrigger(MouseButton button)
    {
        Button = button;
    }

    internal MouseButton Button { get; }
}
