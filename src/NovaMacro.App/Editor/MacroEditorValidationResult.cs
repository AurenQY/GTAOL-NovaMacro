using NovaMacro.App.Configuration;

namespace NovaMacro.App.Editor;

internal sealed class MacroEditorValidationResult
{
    private readonly IReadOnlyList<EditorParseError> _parseErrors;
    private readonly IReadOnlyList<ConfigurationValidationError>
        _configurationErrors;

    private MacroEditorValidationResult(
        ApplicationSettings? settings,
        IEnumerable<EditorParseError> parseErrors,
        IEnumerable<ConfigurationValidationError> configurationErrors)
    {
        Settings = settings;
        _parseErrors = Array.AsReadOnly(parseErrors.ToArray());
        _configurationErrors =
            Array.AsReadOnly(configurationErrors.ToArray());
    }

    internal bool IsValid => Settings is not null;

    internal ApplicationSettings? Settings { get; }

    internal IReadOnlyList<EditorParseError> ParseErrors => _parseErrors;

    internal IReadOnlyList<ConfigurationValidationError> ConfigurationErrors =>
        _configurationErrors;

    internal IEnumerable<string> ErrorMessages =>
        _parseErrors.Select(error => error.Message).Concat(
            _configurationErrors.Select(error => error.Message));

    internal static MacroEditorValidationResult Valid(
        ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new MacroEditorValidationResult(
            settings,
            Array.Empty<EditorParseError>(),
            Array.Empty<ConfigurationValidationError>());
    }

    internal static MacroEditorValidationResult InvalidParse(
        IEnumerable<EditorParseError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        EditorParseError[] copiedErrors = errors.ToArray();
        if (copiedErrors.Length == 0)
        {
            throw new ArgumentException(
                "An invalid editor result requires at least one parse error.",
                nameof(errors));
        }

        return new MacroEditorValidationResult(
            null,
            copiedErrors,
            Array.Empty<ConfigurationValidationError>());
    }

    internal static MacroEditorValidationResult InvalidConfiguration(
        IEnumerable<ConfigurationValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        ConfigurationValidationError[] copiedErrors = errors.ToArray();
        if (copiedErrors.Length == 0)
        {
            throw new ArgumentException(
                "An invalid editor result requires at least one " +
                "configuration error.",
                nameof(errors));
        }

        return new MacroEditorValidationResult(
            null,
            Array.Empty<EditorParseError>(),
            copiedErrors);
    }
}
