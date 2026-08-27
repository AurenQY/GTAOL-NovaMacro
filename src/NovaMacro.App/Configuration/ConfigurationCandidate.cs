using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal sealed record ApplicationSettingsCandidate(
    MacroDefinitionCandidate? Macro);

internal sealed record MacroDefinitionCandidate(
    string? Name,
    KeyboardHotkey? Hotkey,
    MacroSequence? Sequence);
