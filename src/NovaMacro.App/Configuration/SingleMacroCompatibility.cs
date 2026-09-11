namespace NovaMacro.App.Configuration;

internal static class SingleMacroCompatibility
{
    // Temporary bridge for V1 persistence and the single-macro UI/runtime.
    // Remove when MULTI-INTEGRATION-01 moves those consumers to collections.
    internal static MacroId LegacyMacroId { get; } = new(
        new Guid("97d49e4c-8eb2-4aad-8890-c366f29f3b21"));

    internal static MacroDefinition RequireEnabled(
        ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Macros.Count != 1)
        {
            throw new InvalidOperationException(
                "The legacy single-macro path requires exactly one macro; " +
                $"the configuration contains {settings.Macros.Count}.");
        }

        MacroDefinition? onlyMacro = null;

        foreach (MacroDefinition macro in settings.Macros)
        {
            onlyMacro = macro;
        }

        if (!onlyMacro!.Enabled)
        {
            throw new InvalidOperationException(
                "The legacy single-macro path requires its only macro to be " +
                "enabled.");
        }

        return onlyMacro;
    }
}
