using NovaMacro.App.Configuration;
using NovaMacro.App.Presets;

namespace NovaMacro.Tests;

public sealed class StarterConfigurationTests
{
    [Fact]
    public void Default_CreatesValidatedEnabledCopiesOfEveryCatalogPreset()
    {
        ApplicationSettings starter = StarterConfiguration.Default;
        IReadOnlyList<BuiltInPresetDefinition> presets = BuiltInPresetCatalog.All;

        Assert.Equal(4, starter.Macros.Count);
        Assert.Equal(presets.Select(preset => preset.Name),
            starter.Macros.Select(macro => macro.Name));
        Assert.Equal(presets.Select(preset => preset.Trigger),
            starter.Macros.Select(macro => macro.Trigger));
        Assert.Equal(presets.Select(preset => preset.ExecutionMode),
            starter.Macros.Select(macro => macro.ExecutionMode));
        Assert.Equal(presets.Select(preset => preset.Sequence.Steps),
            starter.Macros.Select(macro => macro.Sequence.Steps));
        Assert.All(starter.Macros, macro => Assert.True(macro.Enabled));
        Assert.All(starter.Macros, macro =>
            Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, macro.Id));
        Assert.Equal(starter.Macros.Count,
            starter.Macros.Select(macro => macro.Id).Distinct().Count());

        ConfigurationValidationResult validation = ConfigurationValidator.Validate(
            new ApplicationSettingsCandidate(starter.Macros.Select(macro =>
                new MacroDefinitionCandidate(
                    macro.Id,
                    macro.Name,
                    macro.Enabled,
                    macro.Trigger,
                    macro.ExecutionMode,
                    macro.Sequence)).ToArray()));

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Settings);
        Assert.Empty(validation.Errors);
    }

    [Fact]
    public void Default_CreatesNewOrdinaryIdsForEachFirstRunSnapshot()
    {
        ApplicationSettings first = StarterConfiguration.Default;
        ApplicationSettings second = StarterConfiguration.Default;

        Assert.Empty(first.Macros.Select(macro => macro.Id).Intersect(
            second.Macros.Select(macro => macro.Id)));
    }
}
