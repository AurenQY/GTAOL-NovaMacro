using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal sealed class ApplicationSettings
{
    private readonly IReadOnlyList<MacroDefinition> _macros;

    internal ApplicationSettings(IEnumerable<MacroDefinition> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);

        MacroDefinition[] copiedMacros = macros.ToArray();
        if (copiedMacros.Any(macro => macro is null))
        {
            throw new ArgumentException(
                "Application settings cannot contain a null macro.",
                nameof(macros));
        }

        _macros = Array.AsReadOnly(copiedMacros);
    }

    internal IReadOnlyList<MacroDefinition> Macros => _macros;
}

internal sealed class MacroDefinition
{
    internal MacroDefinition(
        MacroId id,
        string name,
        bool enabled,
        MacroTrigger trigger,
        MacroExecutionMode executionMode,
        MacroSequence sequence)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "A macro ID cannot be empty.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(sequence);

        Id = id;
        Name = name;
        Enabled = enabled;
        Trigger = trigger;
        ExecutionMode = executionMode;
        Sequence = sequence;
    }

    internal MacroDefinition(
        MacroId id,
        string name,
        bool enabled,
        KeyboardHotkey hotkey,
        MacroSequence sequence)
        : this(
            id,
            name,
            enabled,
            new KeyboardMacroTrigger(hotkey),
            MacroExecutionMode.Once,
            sequence)
    {
    }

    internal MacroId Id { get; }

    internal string Name { get; }

    internal bool Enabled { get; }

    internal MacroTrigger Trigger { get; }

    internal MacroExecutionMode ExecutionMode { get; }

    internal MacroSequence Sequence { get; }
}
