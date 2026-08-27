using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal static class ConfigurationValidator
{
    private const int MaximumMacroNameLength = 64;
    private const int MinimumMacroStepCount = 1;
    private const int MaximumMacroStepCount = 100;

    private static readonly TimeSpan MinimumDelay =
        TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaximumDelay =
        TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaximumTotalDelay =
        TimeSpan.FromMinutes(5);

    internal static ConfigurationValidationResult Validate(
        ApplicationSettingsCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = new List<ConfigurationValidationError>();
        MacroDefinitionCandidate? macro = candidate.Macro;

        if (macro is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroRequired,
                "macro",
                "A macro configuration is required."));

            return ConfigurationValidationResult.Invalid(errors);
        }

        string normalizedName = ValidateName(macro.Name, errors);
        ValidateHotkey(macro.Hotkey, errors);
        ValidateSequence(macro.Sequence, errors);

        if (errors.Count != 0)
        {
            return ConfigurationValidationResult.Invalid(errors);
        }

        var definition = new MacroDefinition(
            normalizedName,
            macro.Hotkey!,
            macro.Sequence!);

        return ConfigurationValidationResult.Valid(
            new ApplicationSettings(definition));
    }

    private static string ValidateName(
        string? name,
        ICollection<ConfigurationValidationError> errors)
    {
        string normalizedName = name?.Trim() ?? string.Empty;

        if (normalizedName.Length == 0)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroNameRequired,
                "macro.name",
                "Macro name is required."));
        }

        if (normalizedName.Length > MaximumMacroNameLength)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroNameTooLong,
                "macro.name",
                $"Macro name must be {MaximumMacroNameLength} characters or fewer."));
        }

        if (normalizedName.Any(char.IsControl))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroNameContainsControlCharacter,
                "macro.name",
                "Macro name cannot contain control characters."));
        }

        return normalizedName;
    }

    private static void ValidateHotkey(
        KeyboardHotkey? hotkey,
        ICollection<ConfigurationValidationError> errors)
    {
        if (hotkey is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.HotkeyRequired,
                "macro.hotkey",
                "A macro hotkey is required."));
            return;
        }

        if (hotkey.Modifiers == HotkeyModifiers.None &&
            IsLetterHotkey(hotkey.Key))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
                "macro.hotkey",
                "A letter hotkey must include Ctrl, Alt, or Shift."));
        }
    }

    private static void ValidateSequence(
        MacroSequence? sequence,
        ICollection<ConfigurationValidationError> errors)
    {
        if (sequence is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroSequenceRequired,
                "macro.sequence",
                "A macro sequence is required."));
            return;
        }

        IReadOnlyList<MacroStep> steps = sequence.Steps;
        if (steps.Count < MinimumMacroStepCount ||
            steps.Count > MaximumMacroStepCount)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
                "macro.sequence.steps",
                $"A macro must contain between {MinimumMacroStepCount} and " +
                $"{MaximumMacroStepCount} steps."));
        }

        bool containsKeyTap = false;
        long totalDelayTicks = 0;
        bool totalDelayExceeded = false;

        for (int index = 0; index < steps.Count; index++)
        {
            MacroStep step = steps[index];
            string stepPath = $"macro.sequence.steps[{index}]";

            switch (step)
            {
                case KeyTapStep keyTap:
                    containsKeyTap = true;
                    ValidateOutputKey(keyTap, stepPath, errors);
                    break;

                case DelayStep delay:
                    ValidateDelay(delay, stepPath, errors);
                    AddDelayToTotal(
                        delay.Duration,
                        ref totalDelayTicks,
                        ref totalDelayExceeded);
                    break;

                default:
                    errors.Add(new ConfigurationValidationError(
                        ConfigurationValidationErrorCode.UnsupportedMacroStep,
                        stepPath,
                        $"Macro step type '{step.GetType().Name}' is not supported."));
                    break;
            }
        }

        if (!containsKeyTap)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroMustContainKeyTap,
                "macro.sequence.steps",
                "A macro must contain at least one key tap step."));
        }

        if (totalDelayExceeded ||
            totalDelayTicks > MaximumTotalDelay.Ticks)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.TotalDelayTooLong,
                "macro.sequence.steps",
                "Total macro delay must not exceed 300,000 milliseconds."));
        }
    }

    private static void ValidateOutputKey(
        KeyTapStep keyTap,
        string stepPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (!Enum.IsDefined(keyTap.Key))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.UnsupportedOutputKey,
                $"{stepPath}.key",
                $"Output key value '{(int)keyTap.Key}' is not supported."));
        }
    }

    private static void ValidateDelay(
        DelayStep delay,
        string stepPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (delay.Duration < MinimumDelay ||
            delay.Duration > MaximumDelay)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.DelayOutOfRange,
                $"{stepPath}.duration",
                "Each delay must be between 1 and 60,000 milliseconds."));
        }
    }

    private static void AddDelayToTotal(
        TimeSpan duration,
        ref long totalDelayTicks,
        ref bool totalDelayExceeded)
    {
        if (totalDelayExceeded)
        {
            return;
        }

        if (duration.Ticks > MaximumTotalDelay.Ticks - totalDelayTicks)
        {
            totalDelayExceeded = true;
            return;
        }

        totalDelayTicks += duration.Ticks;
    }

    private static bool IsLetterHotkey(HotkeyKey key)
    {
        return key >= HotkeyKey.A && key <= HotkeyKey.Z;
    }
}
