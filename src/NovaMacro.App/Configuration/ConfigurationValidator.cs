using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration;

internal static class ConfigurationValidator
{
    private const int MaximumMacroCount = 32;
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
        IReadOnlyList<MacroDefinitionCandidate?>? macros = candidate.Macros;

        if (macros is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacrosRequired,
                "macros",
                "A macro collection is required."));

            return ConfigurationValidationResult.Invalid(errors);
        }

        if (macros.Count > MaximumMacroCount)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroCountOutOfRange,
                "macros",
                $"Application settings support between 0 and " +
                $"{MaximumMacroCount} macros."));
        }

        var normalizedNames = new string[macros.Count];
        var seenIds = new HashSet<MacroId>();
        var enabledHotkeys = new HashSet<KeyboardHotkey>();

        for (int index = 0; index < macros.Count; index++)
        {
            MacroDefinitionCandidate? macro = macros[index];
            string macroPath = $"macros[{index}]";

            if (macro is null)
            {
                errors.Add(new ConfigurationValidationError(
                    ConfigurationValidationErrorCode.MacroRequired,
                    macroPath,
                    "A macro configuration is required."));
                continue;
            }

            ValidateId(macro.Id, macroPath, seenIds, errors);
            normalizedNames[index] =
                ValidateName(macro.Name, macroPath, errors);
            ValidateHotkey(macro.Hotkey, macroPath, errors);
            ValidateEnabledHotkeyUniqueness(
                macro.Enabled,
                macro.Hotkey,
                macroPath,
                enabledHotkeys,
                errors);
            ValidateSequence(macro.Sequence, macroPath, errors);
        }

        if (errors.Count != 0)
        {
            return ConfigurationValidationResult.Invalid(errors);
        }

        var definitions = new List<MacroDefinition>(macros.Count);

        for (int index = 0; index < macros.Count; index++)
        {
            MacroDefinitionCandidate macro = macros[index]!;
            definitions.Add(new MacroDefinition(
                macro.Id!.Value,
                normalizedNames[index],
                macro.Enabled,
                macro.Hotkey!,
                macro.Sequence!));
        }

        return ConfigurationValidationResult.Valid(
            new ApplicationSettings(definitions));
    }

    private static void ValidateId(
        MacroId? id,
        string macroPath,
        ISet<MacroId> seenIds,
        ICollection<ConfigurationValidationError> errors)
    {
        if (id is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroIdRequired,
                $"{macroPath}.id",
                "A macro ID is required."));
            return;
        }

        if (id.Value.IsEmpty)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroIdEmpty,
                $"{macroPath}.id",
                "A macro ID cannot be empty."));
            return;
        }

        if (!seenIds.Add(id.Value))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.DuplicateMacroId,
                $"{macroPath}.id",
                $"Macro ID '{id.Value}' is duplicated."));
        }
    }

    private static string ValidateName(
        string? name,
        string macroPath,
        ICollection<ConfigurationValidationError> errors)
    {
        string normalizedName = name?.Trim() ?? string.Empty;

        if (normalizedName.Length == 0)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroNameRequired,
                $"{macroPath}.name",
                "Macro name is required."));
        }

        if (normalizedName.Length > MaximumMacroNameLength)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroNameTooLong,
                $"{macroPath}.name",
                $"Macro name must be {MaximumMacroNameLength} characters or fewer."));
        }

        if (normalizedName.Any(char.IsControl))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroNameContainsControlCharacter,
                $"{macroPath}.name",
                "Macro name cannot contain control characters."));
        }

        return normalizedName;
    }

    private static void ValidateHotkey(
        KeyboardHotkey? hotkey,
        string macroPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (hotkey is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.HotkeyRequired,
                $"{macroPath}.hotkey",
                "A macro hotkey is required."));
            return;
        }

        if (hotkey.Modifiers == HotkeyModifiers.None &&
            IsLetterHotkey(hotkey.Key))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
                $"{macroPath}.hotkey",
                "A letter hotkey must include Ctrl, Alt, or Shift."));
        }
    }

    private static void ValidateEnabledHotkeyUniqueness(
        bool enabled,
        KeyboardHotkey? hotkey,
        string macroPath,
        ISet<KeyboardHotkey> enabledHotkeys,
        ICollection<ConfigurationValidationError> errors)
    {
        if (!enabled || hotkey is null)
        {
            return;
        }

        if (!enabledHotkeys.Add(hotkey))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.DuplicateEnabledHotkey,
                $"{macroPath}.hotkey",
                "Enabled macros must use unique hotkeys."));
        }
    }

    private static void ValidateSequence(
        MacroSequence? sequence,
        string macroPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (sequence is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroSequenceRequired,
                $"{macroPath}.steps",
                "A macro sequence is required."));
            return;
        }

        IReadOnlyList<MacroStep> steps = sequence.Steps;
        if (steps.Count < MinimumMacroStepCount ||
            steps.Count > MaximumMacroStepCount)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
                $"{macroPath}.steps",
                $"A macro must contain between {MinimumMacroStepCount} and " +
                $"{MaximumMacroStepCount} steps."));
        }

        bool containsKeyTap = false;
        long totalDelayTicks = 0;
        bool totalDelayExceeded = false;

        for (int index = 0; index < steps.Count; index++)
        {
            MacroStep step = steps[index];
            string stepPath = $"{macroPath}.steps[{index}]";

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
                $"{macroPath}.steps",
                "A macro must contain at least one key tap step."));
        }

        if (totalDelayExceeded ||
            totalDelayTicks > MaximumTotalDelay.Ticks)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.TotalDelayTooLong,
                $"{macroPath}.steps",
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
