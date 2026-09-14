using NovaMacro.Core;
using NovaMacro.Windows;
using NovaMacro.App.Configuration;

namespace NovaMacro.App.Presets;

internal sealed class BuiltInPresetDefinition
{
    internal BuiltInPresetDefinition(
        string id,
        string name,
        bool enabled,
        MacroTrigger trigger,
        MacroExecutionMode executionMode,
        MacroSequence sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(sequence);

        Id = id;
        Name = name;
        Enabled = enabled;
        Trigger = trigger;
        ExecutionMode = executionMode;
        Sequence = sequence;
    }

    internal BuiltInPresetDefinition(
        string id,
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

    internal string Id { get; }

    internal string Name { get; }

    internal bool Enabled { get; }

    internal MacroTrigger Trigger { get; }

    internal MacroExecutionMode ExecutionMode { get; }

    internal MacroSequence Sequence { get; }

    public override string ToString()
    {
        return Name;
    }
}

internal static class BuiltInPresetCatalog
{
    internal const string BuyAllAmmoPresetId = "gta-buy-all-ammo";
    internal const string DropBullSharkPresetId = "gta-drop-bull-shark";
    internal const string RequestSparrowPresetId = "gta-request-sparrow";
    internal const string HeavyWeaponRapidFirePresetId =
        "gta-heavy-weapon-rapid-fire";

    // These are initial smoke-test tuning values, not final GTA timings.
    private static readonly TimeSpan InitialMenuOpenDelay =
        TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan InitialDirectionalTapDelay =
        TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan InitialSubmenuTransitionDelay =
        TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InitialAmmoCompletionDelay =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InitialRapidFireStepDelay =
        TimeSpan.FromMilliseconds(50);

    private static readonly IReadOnlyList<BuiltInPresetDefinition> AllPresets =
        CreateCatalog(
            new BuiltInPresetDefinition(
                BuyAllAmmoPresetId,
                "Buy All Ammo",
                enabled: true,
                new KeyboardMacroTrigger(
                    new KeyboardHotkey(HotkeyKey.R, HotkeyModifiers.Alt)),
                MacroExecutionMode.Once,
                CreateBuyAllAmmoSequence()),
            new BuiltInPresetDefinition(
                DropBullSharkPresetId,
                "Drop Bull Shark",
                enabled: true,
                new KeyboardMacroTrigger(
                    new KeyboardHotkey(HotkeyKey.E, HotkeyModifiers.Alt)),
                MacroExecutionMode.Once,
                CreateDropBullSharkSequence()),
            new BuiltInPresetDefinition(
                RequestSparrowPresetId,
                "Request Sparrow",
                enabled: true,
                new KeyboardMacroTrigger(
                    new KeyboardHotkey(
                        HotkeyKey.H,
                        HotkeyModifiers.Control | HotkeyModifiers.Alt)),
                MacroExecutionMode.Once,
                CreateRequestSparrowSequence()),
            new BuiltInPresetDefinition(
                HeavyWeaponRapidFirePresetId,
                "Heavy Weapon Rapid Fire",
                enabled: true,
                new MouseButtonMacroTrigger(MouseButton.XButton1),
                MacroExecutionMode.RepeatWhileHeld,
                CreateHeavyWeaponRapidFireSequence()));

    internal static IReadOnlyList<BuiltInPresetDefinition> All => AllPresets;

    private static MacroSequence CreateBuyAllAmmoSequence()
    {
        return new MacroSequence(new MacroStep[]
        {
            Key(KeyboardKey.M),
            Delay(InitialMenuOpenDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter),
            Delay(InitialSubmenuTransitionDelay),
            Key(KeyboardKey.Enter),
            Delay(InitialSubmenuTransitionDelay),
            Key(KeyboardKey.ArrowLeft),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter),
            Delay(InitialAmmoCompletionDelay),
            Key(KeyboardKey.M)
        });
    }

    private static MacroSequence CreateDropBullSharkSequence()
    {
        return new MacroSequence(new MacroStep[]
        {
            Key(KeyboardKey.M),
            Delay(InitialMenuOpenDelay),
            Key(KeyboardKey.Enter),
            Delay(InitialSubmenuTransitionDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter),
            Delay(InitialSubmenuTransitionDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter)
        });
    }

    private static MacroSequence CreateRequestSparrowSequence()
    {
        return new MacroSequence(new MacroStep[]
        {
            Key(KeyboardKey.M),
            Delay(InitialMenuOpenDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter),
            Delay(InitialSubmenuTransitionDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Delay(InitialDirectionalTapDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter),
            Delay(InitialSubmenuTransitionDelay),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter)
        });
    }

    private static MacroSequence CreateHeavyWeaponRapidFireSequence()
    {
        return new MacroSequence(new MacroStep[]
        {
            new MouseButtonTapStep(MouseButton.Left),
            Delay(InitialRapidFireStepDelay),
            Key(KeyboardKey.Digit5),
            Delay(InitialRapidFireStepDelay),
            Key(KeyboardKey.Digit4),
            Delay(InitialRapidFireStepDelay)
        });
    }

    private static KeyTapStep Key(KeyboardKey key) => new(key);

    private static DelayStep Delay(TimeSpan duration) => new(duration);

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
