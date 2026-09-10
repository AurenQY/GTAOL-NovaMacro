using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Storage;

namespace NovaMacro.App.Startup;

internal sealed class StartupConfigurationCoordinator
{
    private readonly ISettingsStore _settingsStore;

    internal StartupConfigurationCoordinator(ISettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        _settingsStore = settingsStore;
    }

    internal StartupConfigurationResult Resolve()
    {
        SettingsLoadResult loadResult = _settingsStore.Load();
        ApplicationSettings starterSettings = StarterConfiguration.Default;

        return loadResult.Status switch
        {
            SettingsLoadStatus.Missing =>
                StartupConfigurationResult.StarterReady(
                    starterSettings,
                    _settingsStore.SettingsPath),

            SettingsLoadStatus.Loaded =>
                StartupConfigurationResult.SavedReady(
                    loadResult.Settings ?? throw InvalidLoadResult(
                        "Loaded settings are missing."),
                    _settingsStore.SettingsPath),

            SettingsLoadStatus.Invalid =>
                StartupConfigurationResult.InvalidRecovery(
                    starterSettings,
                    _settingsStore.SettingsPath,
                    loadResult.JsonReadResult,
                    loadResult.Error),

            SettingsLoadStatus.Unavailable =>
                StartupConfigurationResult.UnavailableRecovery(
                    starterSettings,
                    _settingsStore.SettingsPath,
                    loadResult.Error ?? throw InvalidLoadResult(
                        "Unavailable settings require a storage error.")),

            _ => throw InvalidLoadResult(
                $"Unsupported settings load status '{loadResult.Status}'.")
        };
    }

    private static InvalidOperationException InvalidLoadResult(string message)
    {
        return new InvalidOperationException(
            $"The settings store returned an invalid load result. {message}");
    }
}
