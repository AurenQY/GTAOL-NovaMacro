using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;

namespace NovaMacro.App.Startup;

internal enum StartupConfigurationStatus
{
    StarterReady,
    SavedReady,
    InvalidRecovery,
    UnavailableRecovery
}

internal sealed class StartupConfigurationResult
{
    private readonly IReadOnlyList<string> _diagnostics;

    private StartupConfigurationResult(
        StartupConfigurationStatus status,
        ApplicationSettings initialEditorSettings,
        ApplicationSettings? runtimeSettings,
        string settingsPath,
        SettingsJsonReadResult? jsonReadResult,
        SettingsStorageError? storageError,
        IEnumerable<string> diagnostics)
    {
        Status = status;
        InitialEditorSettings = initialEditorSettings;
        RuntimeSettings = runtimeSettings;
        SettingsPath = settingsPath;
        JsonReadResult = jsonReadResult;
        StorageError = storageError;
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    internal StartupConfigurationStatus Status { get; }

    internal ApplicationSettings InitialEditorSettings { get; }

    internal ApplicationSettings? RuntimeSettings { get; }

    internal string SettingsPath { get; }

    internal SettingsJsonReadResult? JsonReadResult { get; }

    internal SettingsStorageError? StorageError { get; }

    internal IReadOnlyList<string> Diagnostics => _diagnostics;

    internal static StartupConfigurationResult StarterReady(
        ApplicationSettings starterSettings,
        string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(starterSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        return new StartupConfigurationResult(
            StartupConfigurationStatus.StarterReady,
            starterSettings,
            starterSettings,
            settingsPath,
            null,
            null,
            Array.Empty<string>());
    }

    internal static StartupConfigurationResult SavedReady(
        ApplicationSettings savedSettings,
        string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(savedSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        return new StartupConfigurationResult(
            StartupConfigurationStatus.SavedReady,
            savedSettings,
            savedSettings,
            settingsPath,
            null,
            null,
            Array.Empty<string>());
    }

    internal static StartupConfigurationResult InvalidRecovery(
        ApplicationSettings starterSettings,
        string settingsPath,
        SettingsJsonReadResult? jsonReadResult,
        SettingsStorageError? storageError)
    {
        ArgumentNullException.ThrowIfNull(starterSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        if (jsonReadResult is null && storageError is null)
        {
            throw new ArgumentException(
                "Invalid recovery requires JSON or storage diagnostics.");
        }

        return new StartupConfigurationResult(
            StartupConfigurationStatus.InvalidRecovery,
            starterSettings,
            null,
            settingsPath,
            jsonReadResult,
            storageError,
            BuildInvalidDiagnostics(
                settingsPath,
                jsonReadResult,
                storageError));
    }

    internal static StartupConfigurationResult UnavailableRecovery(
        ApplicationSettings starterSettings,
        string settingsPath,
        SettingsStorageError storageError)
    {
        ArgumentNullException.ThrowIfNull(starterSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(storageError);

        return new StartupConfigurationResult(
            StartupConfigurationStatus.UnavailableRecovery,
            starterSettings,
            null,
            settingsPath,
            null,
            storageError,
            new[]
            {
                $"Settings path: {settingsPath}",
                $"{storageError.Code}: {storageError.Message}"
            });
    }

    private static IEnumerable<string> BuildInvalidDiagnostics(
        string settingsPath,
        SettingsJsonReadResult? jsonReadResult,
        SettingsStorageError? storageError)
    {
        yield return $"Settings path: {settingsPath}";

        if (storageError is not null)
        {
            yield return $"{storageError.Code}: {storageError.Message}";
        }

        if (jsonReadResult is null)
        {
            yield break;
        }

        foreach (SettingsJsonError error in jsonReadResult.CodecErrors)
        {
            yield return $"{error.Code} at {error.Path}: {error.Message}";
        }

        foreach (ConfigurationValidationError error in
                 jsonReadResult.ValidationErrors)
        {
            yield return $"{error.Code} at {error.Path}: {error.Message}";
        }
    }
}
