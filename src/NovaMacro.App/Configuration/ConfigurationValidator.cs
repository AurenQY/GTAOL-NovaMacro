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
        var enabledTriggers = new HashSet<MacroTrigger>();

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
            ValidateTrigger(macro.Trigger, macroPath, errors);
            ValidateExecutionMode(
                macro.Trigger,
                macro.ExecutionMode,
                macroPath,
                errors);
            ValidateEnabledTriggerUniqueness(
                macro.Enabled,
                macro.Trigger,
                macroPath,
                enabledTriggers,
                errors);
            ValidateSequence(
                macro.Sequence,
                macro.ExecutionMode,
                macroPath,
                errors);
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
                macro.Trigger!,
                macro.ExecutionMode,
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

    private static void ValidateTrigger(
        MacroTrigger? trigger,
        string macroPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (trigger is null)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.TriggerRequired,
                $"{macroPath}.trigger",
                "A macro trigger is required."));
            return;
        }

        switch (trigger)
        {
            case KeyboardMacroTrigger keyboard
                when keyboard.Hotkey.Modifiers == HotkeyModifiers.None &&
                    IsLetterHotkey(keyboard.Hotkey.Key):
                errors.Add(new ConfigurationValidationError(
                    ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
                    $"{macroPath}.trigger.hotkey",
                    "A letter hotkey must include Ctrl, Alt, or Shift."));
                break;

            case KeyboardMacroTrigger:
                break;

            case MouseButtonMacroTrigger mouse
                when mouse.Button is not MouseButton.XButton1 and
                    not MouseButton.XButton2:
                errors.Add(new ConfigurationValidationError(
                    ConfigurationValidationErrorCode.UnsupportedMouseTriggerButton,
                    $"{macroPath}.trigger.button",
                    "Only Mouse4 and Mouse5 can be macro triggers."));
                break;

            case MouseButtonMacroTrigger:
                break;

            default:
                errors.Add(new ConfigurationValidationError(
                    ConfigurationValidationErrorCode.UnsupportedTrigger,
                    $"{macroPath}.trigger",
                    $"Macro trigger type '{trigger.GetType().Name}' is not supported."));
                break;
        }
    }

    private static void ValidateExecutionMode(
        MacroTrigger? trigger,
        MacroExecutionMode executionMode,
        string macroPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (!Enum.IsDefined(executionMode))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.UnsupportedExecutionMode,
                $"{macroPath}.executionMode",
                $"Execution mode value '{(int)executionMode}' is not supported."));
            return;
        }

        if (executionMode == MacroExecutionMode.RepeatWhileHeld &&
            trigger is KeyboardMacroTrigger)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.KeyboardRepeatWhileHeldNotAllowed,
                $"{macroPath}.executionMode",
                "Repeat while held is supported only for Mouse4 or Mouse5 triggers."));
        }
    }

    private static void ValidateEnabledTriggerUniqueness(
        bool enabled,
        MacroTrigger? trigger,
        string macroPath,
        ISet<MacroTrigger> enabledTriggers,
        ICollection<ConfigurationValidationError> errors)
    {
        if (!enabled || trigger is null)
        {
            return;
        }

        if (!enabledTriggers.Add(trigger))
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.DuplicateEnabledTrigger,
                $"{macroPath}.trigger",
                "Enabled macros must use unique triggers."));
        }
    }

    private static void ValidateSequence(
        MacroSequence? sequence,
        MacroExecutionMode executionMode,
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
        if (executionMode == MacroExecutionMode.RepeatWhileHeld &&
            steps.Count == 0)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.EmptyRepeatingSequenceNotAllowed,
                $"{macroPath}.steps",
                "A repeat-while-held macro cannot have an empty sequence."));
        }
        if (steps.Count < MinimumMacroStepCount ||
            steps.Count > MaximumMacroStepCount)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
                $"{macroPath}.steps",
                $"A macro must contain between {MinimumMacroStepCount} and " +
                $"{MaximumMacroStepCount} steps."));
        }

        bool containsInputTap = false;
        long totalDelayTicks = 0;
        bool totalDelayExceeded = false;

        for (int index = 0; index < steps.Count; index++)
        {
            MacroStep step = steps[index];
            string stepPath = $"{macroPath}.steps[{index}]";

            switch (step)
            {
                case KeyTapStep keyTap:
                    containsInputTap = true;
                    ValidateOutputKey(keyTap, stepPath, errors);
                    break;

                case MouseButtonTapStep mouseTap:
                    containsInputTap = true;
                    ValidateOutputMouseButton(mouseTap, stepPath, errors);
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

        if (!containsInputTap)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.MacroMustContainKeyTap,
                $"{macroPath}.steps",
                "A macro must contain at least one keyboard or mouse tap step."));
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

    private static void ValidateOutputMouseButton(
        MouseButtonTapStep mouseTap,
        string stepPath,
        ICollection<ConfigurationValidationError> errors)
    {
        if (mouseTap.Button != MouseButton.Left)
        {
            errors.Add(new ConfigurationValidationError(
                ConfigurationValidationErrorCode.UnsupportedOutputMouseButton,
                $"{stepPath}.button",
                $"Output mouse button '{mouseTap.Button}' is not supported."));
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
