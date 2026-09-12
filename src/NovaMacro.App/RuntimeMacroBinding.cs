using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App;

internal sealed record RuntimeMacroBinding(
    MacroId Id,
    KeyboardHotkey Hotkey,
    MacroSequence Sequence);
