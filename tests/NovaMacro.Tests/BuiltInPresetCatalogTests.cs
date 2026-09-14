using System.Text.Json;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Editor;
using NovaMacro.App.Presets;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class BuiltInPresetCatalogTests
{
    [Fact]
    public void Catalog_ContainsExactlyTheApprovedGtaPresetsInProductOrder()
    {
        IReadOnlyList<BuiltInPresetDefinition> catalog = BuiltInPresetCatalog.All;

        Assert.Equal(
            new[]
            {
                "Buy All Ammo",
                "Drop Bull Shark",
                "Request Sparrow",
                "Heavy Weapon Rapid Fire"
            },
            catalog.Select(preset => preset.Name));
        Assert.Equal(
            new[]
            {
                "gta-buy-all-ammo",
                "gta-drop-bull-shark",
                "gta-request-sparrow",
                "gta-heavy-weapon-rapid-fire"
            },
            catalog.Select(preset => preset.Id));
        Assert.DoesNotContain(catalog, preset => preset.Name == "ABC Test");
        Assert.DoesNotContain(catalog, preset => preset.Id == "abc-test");
        Assert.All(catalog, preset => Assert.True(preset.Enabled));
    }

    [Fact]
    public void BuyAllAmmo_HasExactInitialSequenceAndKeyboardTrigger()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.BuyAllAmmoPresetId);

        Assert.Equal("Buy All Ammo", preset.Name);
        Assert.Equal(
            new KeyboardMacroTrigger(new KeyboardHotkey(
                HotkeyKey.R,
                HotkeyModifiers.Alt)),
            preset.Trigger);
        Assert.Equal(MacroExecutionMode.Once, preset.ExecutionMode);
        Assert.Equal(new MacroStep[]
        {
            Key(KeyboardKey.M), Delay(200),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter), Delay(100),
            Key(KeyboardKey.Enter), Delay(100),
            Key(KeyboardKey.ArrowLeft), Delay(40),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter), Delay(500),
            Key(KeyboardKey.M)
        }, preset.Sequence.Steps);
    }

    [Fact]
    public void DropBullShark_HasExactInitialSequenceIncludingFinalEnter()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.DropBullSharkPresetId);

        Assert.Equal("Drop Bull Shark", preset.Name);
        Assert.Equal(
            new KeyboardMacroTrigger(new KeyboardHotkey(
                HotkeyKey.E,
                HotkeyModifiers.Alt)),
            preset.Trigger);
        Assert.Equal(MacroExecutionMode.Once, preset.ExecutionMode);
        Assert.Equal(new MacroStep[]
        {
            Key(KeyboardKey.M), Delay(200),
            Key(KeyboardKey.Enter), Delay(100),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter), Delay(100),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter)
        }, preset.Sequence.Steps);
        Assert.Equal(new KeyTapStep(KeyboardKey.Enter), preset.Sequence.Steps[^1]);
    }

    [Fact]
    public void RequestSparrow_HasExactInitialSequenceAndKeyboardTrigger()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.RequestSparrowPresetId);

        Assert.Equal("Request Sparrow", preset.Name);
        Assert.Equal(
            new KeyboardMacroTrigger(new KeyboardHotkey(
                HotkeyKey.H,
                HotkeyModifiers.Control | HotkeyModifiers.Alt)),
            preset.Trigger);
        Assert.Equal(MacroExecutionMode.Once, preset.ExecutionMode);
        Assert.Equal(new MacroStep[]
        {
            Key(KeyboardKey.M), Delay(200),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter), Delay(100),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown), Delay(40),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter), Delay(100),
            Key(KeyboardKey.ArrowDown),
            Key(KeyboardKey.Enter)
        }, preset.Sequence.Steps);
        Assert.Equal(8, preset.Sequence.Steps.OfType<KeyTapStep>().Count(
            step => step.Key == KeyboardKey.ArrowDown));
    }

    [Fact]
    public void HeavyWeaponRapidFire_HasExactInitialRepeatWhileHeldIteration()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.HeavyWeaponRapidFirePresetId);

        Assert.Equal("Heavy Weapon Rapid Fire", preset.Name);
        Assert.Equal(
            new MouseButtonMacroTrigger(MouseButton.XButton1),
            preset.Trigger);
        Assert.Equal(MacroExecutionMode.RepeatWhileHeld, preset.ExecutionMode);
        Assert.Equal(new MacroStep[]
        {
            new MouseButtonTapStep(MouseButton.Left), Delay(50),
            Key(KeyboardKey.Digit5), Delay(50),
            Key(KeyboardKey.Digit4), Delay(50)
        }, preset.Sequence.Steps);
    }

    [Fact]
    public void Catalog_ExposesAnImmutableCollection()
    {
        IReadOnlyList<BuiltInPresetDefinition> catalog = BuiltInPresetCatalog.All;

        if (catalog is IList<BuiltInPresetDefinition> mutableView)
        {
            Assert.True(mutableView.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => mutableView.Clear());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Definition_RejectsBlankStableIdentityOrDisplayName(string blank)
    {
        Assert.Throws<ArgumentException>(() => new BuiltInPresetDefinition(
            blank,
            "Name",
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            Sequence(KeyboardKey.A)));
        Assert.Throws<ArgumentException>(() => new BuiltInPresetDefinition(
            "preset",
            blank,
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            Sequence(KeyboardKey.A)));
    }

    [Fact]
    public void AddFromPreset_CreatesIndependentEditableCopiesWithFreshIds()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.BuyAllAmmoPresetId);
        var draft = MultiMacroEditorDraft.From(EmptySettings());

        MacroEditorDraft first = draft.AddFromPreset(preset);
        MacroEditorDraft second = draft.AddFromPreset(preset);

        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.Id.IsEmpty);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, first.Id);
        Assert.Equal(new[] { "Buy All Ammo", "Buy All Ammo" },
            draft.Macros.Select(macro => macro.Name));
        Assert.Same(second, draft.SelectedMacro);

        first.Name = "Edited copy";
        first.Enabled = false;
        first.Steps[0].Key = KeyboardKey.Z;

        Assert.Equal("Buy All Ammo", preset.Name);
        Assert.Equal(new KeyTapStep(KeyboardKey.M), preset.Sequence.Steps[0]);
        Assert.Equal("Buy All Ammo", second.Name);
        Assert.True(second.Enabled);
        Assert.Equal(KeyboardKey.M, second.Steps[0].Key);
    }

    [Theory]
    [InlineData("gta-buy-all-ammo")]
    [InlineData("gta-heavy-weapon-rapid-fire")]
    public void DuplicateEnabledPresetCopies_UseCanonicalTriggerValidation(
        string presetId)
    {
        BuiltInPresetDefinition preset = Preset(presetId);
        var draft = MultiMacroEditorDraft.From(EmptySettings());
        draft.AddFromPreset(preset);
        draft.AddFromPreset(preset);

        MacroEditorValidationResult validation = draft.Validate();

        ConfigurationValidationError error = Assert.Single(
            validation.ConfigurationErrors);
        Assert.Equal(ConfigurationValidationErrorCode.DuplicateEnabledTrigger, error.Code);
        Assert.Equal("macros[1].trigger", error.Path);
    }

    [Fact]
    public void PresetCopy_RoundTripsAsAnOrdinaryV3MacroWithoutMetadata()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.RequestSparrowPresetId);
        var draft = MultiMacroEditorDraft.From(EmptySettings());
        MacroEditorDraft copy = draft.AddFromPreset(preset);

        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.True(validation.IsValid);

        string json = SettingsJsonCodec.WriteV3(settings!);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(
            SettingsJsonCodec.ReadV1OrV2OrV3(json).Settings);
        MacroDefinition macro = Assert.Single(restored.Macros);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(copy.Id, macro.Id);
        Assert.Equal(preset.Trigger, macro.Trigger);
        Assert.Equal(preset.ExecutionMode, macro.ExecutionMode);
        Assert.Equal(preset.Sequence.Steps, macro.Sequence.Steps);
        Assert.Equal(
            new[] { "id", "name", "enabled", "trigger", "executionMode", "steps" },
            document.RootElement.GetProperty("macros").EnumerateArray().Single()
                .EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("preset", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddFromPreset_AllowsTemporaryThirtyThirdDraftBeforeValidationRejectsIt()
    {
        BuiltInPresetDefinition preset = Preset(
            BuiltInPresetCatalog.BuyAllAmmoPresetId);
        var draft = MultiMacroEditorDraft.From(EmptySettings());

        for (int index = 0; index < 33; index++)
        {
            MacroEditorDraft added = draft.AddFromPreset(preset);
            if (index != 0)
            {
                added.Enabled = false;
            }
        }

        Assert.False(draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.Null(settings);
        Assert.Contains(validation.ConfigurationErrors,
            error => error.Code == ConfigurationValidationErrorCode.MacroCountOutOfRange);
    }

    private static BuiltInPresetDefinition Preset(string id) =>
        Assert.Single(BuiltInPresetCatalog.All, preset => preset.Id == id);

    private static ApplicationSettings EmptySettings() =>
        new(Array.Empty<MacroDefinition>());

    private static MacroSequence Sequence(KeyboardKey key) =>
        new(new MacroStep[] { Key(key) });

    private static KeyTapStep Key(KeyboardKey key) => new(key);

    private static DelayStep Delay(int milliseconds) =>
        new(TimeSpan.FromMilliseconds(milliseconds));
}
