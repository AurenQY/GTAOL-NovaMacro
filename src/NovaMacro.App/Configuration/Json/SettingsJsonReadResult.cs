namespace NovaMacro.App.Configuration.Json;

internal enum SettingsJsonErrorCode
{
    InvalidJson,
    ExpectedObject,
    ExpectedArray,
    ExpectedString,
    ExpectedBoolean,
    ExpectedInteger,
    IntegerOutOfRange,
    MissingRequiredProperty,
    UnknownProperty,
    DuplicateProperty,
    UnsupportedVersion,
    InvalidMacroId,
    UnknownHotkeyKey,
    UnknownModifier,
    DuplicateModifier,
    UnknownStepType,
    UnknownOutputKey,
    DelayMillisecondsOutOfRange
}

internal sealed record SettingsJsonError(
    SettingsJsonErrorCode Code,
    string Path,
    string Message);

internal sealed class SettingsJsonReadResult
{
    private readonly IReadOnlyList<SettingsJsonError> _codecErrors;
    private readonly IReadOnlyList<ConfigurationValidationError>
        _validationErrors;

    private SettingsJsonReadResult(
        ApplicationSettings? settings,
        IEnumerable<SettingsJsonError> codecErrors,
        IEnumerable<ConfigurationValidationError> validationErrors)
    {
        Settings = settings;
        _codecErrors = Array.AsReadOnly(codecErrors.ToArray());
        _validationErrors = Array.AsReadOnly(validationErrors.ToArray());
    }

    internal bool IsSuccess => Settings is not null;

    internal ApplicationSettings? Settings { get; }

    internal IReadOnlyList<SettingsJsonError> CodecErrors => _codecErrors;

    internal IReadOnlyList<ConfigurationValidationError> ValidationErrors =>
        _validationErrors;

    internal static SettingsJsonReadResult CodecFailure(
        SettingsJsonError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new SettingsJsonReadResult(
            null,
            new[] { error },
            Array.Empty<ConfigurationValidationError>());
    }

    internal static SettingsJsonReadResult FromValidation(
        ConfigurationValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        return validationResult.IsValid
            ? new SettingsJsonReadResult(
                validationResult.Settings,
                Array.Empty<SettingsJsonError>(),
                Array.Empty<ConfigurationValidationError>())
            : new SettingsJsonReadResult(
                null,
                Array.Empty<SettingsJsonError>(),
                validationResult.Errors);
    }
}
