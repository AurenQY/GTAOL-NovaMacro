namespace NovaMacro.App.Configuration;

internal enum ConfigurationValidationErrorCode
{
    MacrosRequired,
    MacroCountOutOfRange,
    MacroRequired,
    MacroIdRequired,
    MacroIdEmpty,
    DuplicateMacroId,
    DuplicateEnabledHotkey,
    MacroNameRequired,
    MacroNameTooLong,
    MacroNameContainsControlCharacter,
    HotkeyRequired,
    ModifierlessLetterHotkeyNotAllowed,
    MacroSequenceRequired,
    MacroStepCountOutOfRange,
    MacroMustContainKeyTap,
    DelayOutOfRange,
    TotalDelayTooLong,
    UnsupportedMacroStep,
    UnsupportedOutputKey
}

internal sealed record ConfigurationValidationError(
    ConfigurationValidationErrorCode Code,
    string Path,
    string Message);

internal sealed class ConfigurationValidationResult
{
    private readonly IReadOnlyList<ConfigurationValidationError> _errors;

    private ConfigurationValidationResult(
        ApplicationSettings? settings,
        IEnumerable<ConfigurationValidationError> errors)
    {
        Settings = settings;
        _errors = Array.AsReadOnly(errors.ToArray());
    }

    internal bool IsValid => Settings is not null;

    internal ApplicationSettings? Settings { get; }

    internal IReadOnlyList<ConfigurationValidationError> Errors => _errors;

    internal static ConfigurationValidationResult Valid(
        ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ConfigurationValidationResult(
            settings,
            Array.Empty<ConfigurationValidationError>());
    }

    internal static ConfigurationValidationResult Invalid(
        IEnumerable<ConfigurationValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        ConfigurationValidationError[] copiedErrors = errors.ToArray();
        if (copiedErrors.Length == 0)
        {
            throw new ArgumentException(
                "An invalid configuration result requires at least one error.",
                nameof(errors));
        }

        return new ConfigurationValidationResult(null, copiedErrors);
    }
}
