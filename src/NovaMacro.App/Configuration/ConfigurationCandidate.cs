using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal sealed record ApplicationSettingsCandidate(
    IReadOnlyList<MacroDefinitionCandidate?>? Macros);

internal sealed record MacroDefinitionCandidate(
    MacroId? Id,
    string? Name,
    bool Enabled,
    MacroTrigger? Trigger,
    MacroExecutionMode ExecutionMode,
    MacroSequence? Sequence)
{
    internal MacroDefinitionCandidate(
        MacroId? id,
        string? name,
        bool enabled,
        KeyboardHotkey? hotkey,
        MacroSequence? sequence)
        : this(
            id,
            name,
            enabled,
            hotkey is null ? null : new KeyboardMacroTrigger(hotkey),
            MacroExecutionMode.Once,
            sequence)
    {
    }
}
