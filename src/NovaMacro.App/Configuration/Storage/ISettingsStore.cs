using NovaMacro.App.Configuration.Json;

namespace NovaMacro.App.Configuration.Storage;

internal interface ISettingsStore
{
    string SettingsPath { get; }

    SettingsLoadResult Load();

    SettingsSaveResult Save(ApplicationSettings settings);
}

internal enum SettingsLoadStatus
{
    Missing,
    Loaded,
    Invalid,
    Unavailable
}

internal enum SettingsStorageErrorCode
{
    FileTooLarge,
    InvalidUtf8,
    ReadFailed,
    DirectoryCreationFailed,
    TemporaryFileWriteFailed,
    PublishFailed
}

internal sealed record SettingsStorageError(
    SettingsStorageErrorCode Code,
    string Path,
    string Message);

internal sealed class SettingsLoadResult
{
    private SettingsLoadResult(
        SettingsLoadStatus status,
        ApplicationSettings? settings,
        SettingsJsonReadResult? jsonReadResult,
        SettingsStorageError? error)
    {
        Status = status;
        Settings = settings;
        JsonReadResult = jsonReadResult;
        Error = error;
    }

    internal SettingsLoadStatus Status { get; }

    internal ApplicationSettings? Settings { get; }

    internal SettingsJsonReadResult? JsonReadResult { get; }

    internal SettingsStorageError? Error { get; }

    internal static SettingsLoadResult Missing()
    {
        return new SettingsLoadResult(
            SettingsLoadStatus.Missing,
            null,
            null,
            null);
    }

    internal static SettingsLoadResult Loaded(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SettingsLoadResult(
            SettingsLoadStatus.Loaded,
            settings,
            null,
            null);
    }

    internal static SettingsLoadResult Invalid(
        SettingsJsonReadResult jsonReadResult)
    {
        ArgumentNullException.ThrowIfNull(jsonReadResult);

        if (jsonReadResult.IsSuccess)
        {
            throw new ArgumentException(
                "A successful JSON result cannot describe invalid settings.",
                nameof(jsonReadResult));
        }

        return new SettingsLoadResult(
            SettingsLoadStatus.Invalid,
            null,
            jsonReadResult,
            null);
    }

    internal static SettingsLoadResult Invalid(SettingsStorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new SettingsLoadResult(
            SettingsLoadStatus.Invalid,
            null,
            null,
            error);
    }

    internal static SettingsLoadResult Unavailable(SettingsStorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new SettingsLoadResult(
            SettingsLoadStatus.Unavailable,
            null,
            null,
            error);
    }
}

internal sealed class SettingsSaveResult
{
    private SettingsSaveResult(SettingsStorageError? error)
    {
        Error = error;
    }

    internal bool IsSuccess => Error is null;

    internal SettingsStorageError? Error { get; }

    internal static SettingsSaveResult Success()
    {
        return new SettingsSaveResult(null);
    }

    internal static SettingsSaveResult Failure(SettingsStorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new SettingsSaveResult(error);
    }
}
