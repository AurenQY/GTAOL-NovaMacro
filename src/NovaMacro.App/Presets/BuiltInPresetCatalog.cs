using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Presets;

internal sealed class BuiltInPresetDefinition
{
    internal BuiltInPresetDefinition(
        string id,
        string name,
        bool enabled,
        KeyboardHotkey hotkey,
        MacroSequence sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(sequence);

        Id = id;
        Name = name;
        Enabled = enabled;
        Hotkey = hotkey;
        Sequence = sequence;
    }

    internal string Id { get; }

    internal string Name { get; }

    internal bool Enabled { get; }

    internal KeyboardHotkey Hotkey { get; }

    internal MacroSequence Sequence { get; }

    public override string ToString()
    {
        return Name;
    }
}

internal static class BuiltInPresetCatalog
{
    internal const string AbcTestPresetId = "abc-test";

    private static readonly IReadOnlyList<BuiltInPresetDefinition> AllPresets =
        CreateCatalog(
            new BuiltInPresetDefinition(
                AbcTestPresetId,
                "ABC Test",
                enabled: true,
                new KeyboardHotkey(HotkeyKey.F8),
                BuiltInMacroSequences.AbcTest));

    internal static IReadOnlyList<BuiltInPresetDefinition> All => AllPresets;

    private static IReadOnlyList<BuiltInPresetDefinition> CreateCatalog(
        params BuiltInPresetDefinition[] presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        if (presets.Any(preset => preset is null))
        {
            throw new ArgumentException(
                "A built-in preset catalog cannot contain a null preset.",
                nameof(presets));
        }

        if (presets.GroupBy(preset => preset.Id, StringComparer.Ordinal).Any(
                group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "Built-in preset IDs must be unique.");
        }

        return Array.AsReadOnly(presets.ToArray());
    }
}
