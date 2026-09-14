using NovaMacro.Core;
using NovaMacro.App.Presets;

namespace NovaMacro.App.Configuration;

internal static class StarterConfiguration
{
    internal static ApplicationSettings Default => CreateDefault();

    private static ApplicationSettings CreateDefault()
    {
        var usedIds = new HashSet<MacroId>();
        var macros = new List<MacroDefinitionCandidate?>();

        foreach (BuiltInPresetDefinition preset in BuiltInPresetCatalog.All)
        {
            macros.Add(new MacroDefinitionCandidate(
                CreateOrdinaryMacroId(usedIds),
                preset.Name,
                preset.Enabled,
                preset.Trigger,
                preset.ExecutionMode,
                preset.Sequence));
        }

        var candidate = new ApplicationSettingsCandidate(macros);

        ConfigurationValidationResult result =
            ConfigurationValidator.Validate(candidate);

        return result.Settings ?? throw new InvalidOperationException(
            "The built-in starter configuration is invalid.");
    }

    private static MacroId CreateOrdinaryMacroId(ISet<MacroId> usedIds)
    {
        MacroId id;

        do
        {
            id = new MacroId(Guid.NewGuid());
        }
        while (id == SingleMacroCompatibility.LegacyMacroId ||
            !usedIds.Add(id));

        return id;
    }
}
