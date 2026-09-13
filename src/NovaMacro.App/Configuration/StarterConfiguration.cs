using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal static class StarterConfiguration
{
    internal static ApplicationSettings Default { get; } = CreateDefault();

    private static ApplicationSettings CreateDefault()
    {
        var candidate = new ApplicationSettingsCandidate(
            new MacroDefinitionCandidate?[]
            {
                new(
                    SingleMacroCompatibility.LegacyMacroId,
                    "ABC Test",
                    true,
                    new KeyboardMacroTrigger(
                        new KeyboardHotkey(HotkeyKey.F8)),
                    MacroExecutionMode.Once,
                    BuiltInMacroSequences.AbcTest)
            });

        ConfigurationValidationResult result =
            ConfigurationValidator.Validate(candidate);

        return result.Settings ?? throw new InvalidOperationException(
            "The built-in starter configuration is invalid.");
    }
}
