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
    public void Catalog_ContainsOnlyTheStableAbcTestTemplate()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);

        Assert.Equal(BuiltInPresetCatalog.AbcTestPresetId, preset.Id);
        Assert.False(string.IsNullOrWhiteSpace(preset.Id));
        Assert.Equal("ABC Test", preset.Name);
        Assert.True(preset.Enabled);
        Assert.Equal(new KeyboardHotkey(HotkeyKey.F8), preset.Hotkey);
        Assert.Equal(BuiltInMacroSequences.AbcTest.Steps, preset.Sequence.Steps);
    }

    [Fact]
    public void Catalog_ExposesAnImmutableCollection()
    {
        IReadOnlyList<BuiltInPresetDefinition> catalog = BuiltInPresetCatalog.All;

        Assert.False(catalog is ICollection<BuiltInPresetDefinition>
        {
            IsReadOnly: false
        });

        if (catalog is IList<BuiltInPresetDefinition> mutableView)
        {
            Assert.True(mutableView.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => mutableView.Clear());
        }

        Assert.Single(catalog);
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
            BuiltInMacroSequences.AbcTest));
        Assert.Throws<ArgumentException>(() => new BuiltInPresetDefinition(
            "preset",
            blank,
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            BuiltInMacroSequences.AbcTest));
    }

    [Fact]
    public void Definition_RejectsNullRequiredValues()
    {
        Assert.Throws<ArgumentNullException>(() => new BuiltInPresetDefinition(
            null!,
            "Name",
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            BuiltInMacroSequences.AbcTest));
        Assert.Throws<ArgumentNullException>(() => new BuiltInPresetDefinition(
            "preset",
            null!,
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            BuiltInMacroSequences.AbcTest));
        Assert.Throws<ArgumentNullException>(() => new BuiltInPresetDefinition(
            "preset",
            "Name",
            true,
            null!,
            BuiltInMacroSequences.AbcTest));
        Assert.Throws<ArgumentNullException>(() => new BuiltInPresetDefinition(
            "preset",
            "Name",
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            null!));
    }

    [Fact]
    public void AbcTemplate_MatchesStarterDefaultsWhileStarterKeepsLegacyIdentity()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);
        MacroDefinition starter = Assert.Single(StarterConfiguration.Default.Macros);

        Assert.Equal(preset.Name, starter.Name);
        Assert.Equal(preset.Enabled, starter.Enabled);
        Assert.Equal(preset.Hotkey, starter.Hotkey);
        Assert.Equal(preset.Sequence.Steps, starter.Sequence.Steps);
        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, starter.Id);
    }

    [Fact]
    public void AddFromPreset_CreatesIndependentOrdinaryCopiesWithFreshIds()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        MacroEditorDraft first = draft.AddFromPreset(preset);
        MacroEditorDraft second = draft.AddFromPreset(preset);

        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.Id.IsEmpty);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, first.Id);
        Assert.Equal(preset.Name, first.Name);
        Assert.Equal(preset.Enabled, first.Enabled);
        Assert.Equal(preset.Hotkey.Key, first.HotkeyKey);
        Assert.False(first.Control);
        Assert.False(first.Alt);
        Assert.False(first.Shift);
        Assert.Equal(new[] { "ABC Test", "ABC Test" },
            draft.Macros.Select(macro => macro.Name));
        Assert.Same(second, draft.SelectedMacro);

        first.Name = "Edited copy";
        first.Enabled = false;
        first.Steps[0].Key = KeyboardKey.Z;

        Assert.Equal("ABC Test", preset.Name);
        Assert.True(preset.Enabled);
        Assert.Equal(new KeyTapStep(KeyboardKey.A), preset.Sequence.Steps[0]);
        Assert.Equal("ABC Test", second.Name);
        Assert.True(second.Enabled);
        Assert.Equal(KeyboardKey.A, second.Steps[0].Key);
    }

    [Fact]
    public void AddFromPreset_RejectsNullAndAcceptsAValidNonCatalogDefinition()
    {
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        Assert.Throws<ArgumentNullException>(() => draft.AddFromPreset(null!));

        var definition = new BuiltInPresetDefinition(
            "test-only",
            "Test only",
            enabled: false,
            new KeyboardHotkey(HotkeyKey.F9, HotkeyModifiers.Control),
            new MacroSequence(new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.Z)
            }));

        MacroEditorDraft added = draft.AddFromPreset(definition);

        Assert.Equal("Test only", added.Name);
        Assert.False(added.Enabled);
        Assert.Equal(HotkeyKey.F9, added.HotkeyKey);
        Assert.True(added.Control);
        Assert.Equal(KeyboardKey.Z, Assert.Single(added.Steps).Key);
    }

    [Fact]
    public void DeleteThenReAdd_PreservesCatalogAndCreatesAnotherFreshId()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        MacroId firstId = draft.AddFromPreset(preset).Id;
        Assert.True(draft.DeleteSelectedMacro());
        MacroEditorDraft readded = draft.AddFromPreset(preset);

        Assert.Single(BuiltInPresetCatalog.All);
        Assert.NotEqual(firstId, readded.Id);
        Assert.Same(readded, draft.SelectedMacro);
    }

    [Fact]
    public void DuplicateEnabledPresetCopies_UseExistingValidationWithoutMutation()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));
        MacroEditorDraft first = draft.AddFromPreset(preset);
        MacroEditorDraft second = draft.AddFromPreset(preset);

        MacroEditorValidationResult validation = draft.Validate();

        ConfigurationValidationError error = Assert.Single(
            validation.ConfigurationErrors);
        Assert.Equal(ConfigurationValidationErrorCode.DuplicateEnabledHotkey, error.Code);
        Assert.Equal("macros[1].hotkey", error.Path);
        Assert.Equal(HotkeyKey.F8, first.HotkeyKey);
        Assert.Equal(HotkeyKey.F8, second.HotkeyKey);
        Assert.True(first.Enabled);
        Assert.True(second.Enabled);
    }

    [Fact]
    public void PresetCopy_RoundTripsAsOrdinaryV2MacroWithoutMetadata()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));
        MacroEditorDraft copy = draft.AddFromPreset(preset);

        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.True(validation.IsValid);

        string json = SettingsJsonCodec.WriteV2(settings!);
        SettingsJsonReadResult read = SettingsJsonCodec.ReadV1OrV2(json);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(read.Settings);
        MacroDefinition macro = Assert.Single(restored.Macros);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement serialized = document.RootElement
            .GetProperty("macros")
            .EnumerateArray()
            .Single();

        Assert.Equal(copy.Id, macro.Id);
        Assert.Equal(preset.Name, macro.Name);
        Assert.Equal(preset.Enabled, macro.Enabled);
        Assert.Equal(preset.Hotkey, macro.Hotkey);
        Assert.Equal(preset.Sequence.Steps, macro.Sequence.Steps);
        Assert.Equal(new[] { "id", "name", "enabled", "hotkey", "steps" },
            serialized.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("preset", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("builtIn", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddFromPreset_AllowsTemporaryThirtyThirdDraftAndValidationReportsCollectionLimit()
    {
        BuiltInPresetDefinition preset = Assert.Single(BuiltInPresetCatalog.All);
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        for (int index = 0; index < 33; index++)
        {
            MacroEditorDraft added = draft.AddFromPreset(preset);
            if (index != 0)
            {
                added.Enabled = false;
            }

            if (index == 31)
            {
                Assert.True(draft.TryBuildSettings(
                    out ApplicationSettings? thirtyTwo,
                    out MacroEditorValidationResult thirtyTwoValidation));
                Assert.True(thirtyTwoValidation.IsValid);
                Assert.Equal(32, thirtyTwo!.Macros.Count);
            }
        }

        Assert.Equal(33, draft.Macros.Count);
        Assert.False(draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.Null(settings);
        Assert.Contains(validation.ConfigurationErrors,
            error => error.Code == ConfigurationValidationErrorCode.MacroCountOutOfRange);
        Assert.Same(draft.Macros[^1], draft.SelectedMacro);
    }
}
