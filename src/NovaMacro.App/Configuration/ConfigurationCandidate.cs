using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal sealed record ApplicationSettingsCandidate(
    IReadOnlyList<MacroDefinitionCandidate?>? Macros);

internal sealed record MacroDefinitionCandidate(
    MacroId? Id,
    string? Name,
    bool Enabled,
    KeyboardHotkey? Hotkey,
    MacroSequence? Sequence);
