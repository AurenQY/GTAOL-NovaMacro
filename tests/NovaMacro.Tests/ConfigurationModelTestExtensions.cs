using NovaMacro.App.Configuration;
using NovaMacro.App.Presets;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal static class ConfigurationModelTestExtensions
{
    internal static KeyboardHotkey GetKeyboardHotkey(
        this MacroDefinition macro)
    {
        return Assert.IsType<KeyboardMacroTrigger>(macro.Trigger).Hotkey;
    }

    internal static KeyboardHotkey GetKeyboardHotkey(
        this MacroDefinitionCandidate macro)
    {
        return Assert.IsType<KeyboardMacroTrigger>(macro.Trigger).Hotkey;
    }

    internal static KeyboardHotkey GetKeyboardHotkey(
        this BuiltInPresetDefinition preset)
    {
        return Assert.IsType<KeyboardMacroTrigger>(preset.Trigger).Hotkey;
    }
}
