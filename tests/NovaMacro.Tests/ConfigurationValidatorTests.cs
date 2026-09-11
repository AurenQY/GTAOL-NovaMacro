using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class ConfigurationValidatorTests
{
    public static TheoryData<string?> RequiredNameCases => new()
    {
        { null },
        { string.Empty },
        { " \t " }
    };

    public static TheoryData<string, bool> NameLengthCases => new()
    {
        { " X ", true },
        { $" {new string('X', 64)} ", true },
        { $" {new string('X', 65)} ", false }
    };

    public static TheoryData<HotkeyKey> LetterHotkeys
    {
        get
        {
            var data = new TheoryData<HotkeyKey>();

            for (HotkeyKey key = HotkeyKey.A; key <= HotkeyKey.Z; key++)
            {
                data.Add(key);
            }

            return data;
        }
    }

    public static TheoryData<HotkeyKey> FunctionHotkeys
    {
        get
        {
            var data = new TheoryData<HotkeyKey>();

            for (HotkeyKey key = HotkeyKey.F1; key <= HotkeyKey.F11; key++)
            {
                data.Add(key);
            }

            return data;
        }
    }

    [Fact]
    public void ValidCandidate_ReturnsNormalizedIndependentSnapshotWithoutMutation()
    {
        const string rawName = "  Useful Macro  ";
        var hotkey = new KeyboardHotkey(
            HotkeyKey.K,
            HotkeyModifiers.Control | HotkeyModifiers.Shift);
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.M),
                new DelayStep(TimeSpan.FromMilliseconds(250))
            });
        var macroCandidate = new MacroDefinitionCandidate(
            SingleMacroCompatibility.LegacyMacroId,
            rawName,
            true,
            hotkey,
            sequence);
        var candidate = new ApplicationSettingsCandidate(
            new MacroDefinitionCandidate?[] { macroCandidate });

        ConfigurationValidationResult result =
            ConfigurationValidator.Validate(candidate);

        ApplicationSettings settings = AssertValid(result);
        MacroDefinition macro = Assert.Single(settings.Macros);
        MacroDefinitionCandidate candidateMacro =
            Assert.Single(candidate.Macros!)!;
        Assert.Equal("Useful Macro", macro.Name);
        Assert.Equal(hotkey, macro.Hotkey);
        Assert.Equal(sequence.Steps, macro.Sequence.Steps);
        Assert.NotSame(macroCandidate, macro);

        Assert.Equal(rawName, candidateMacro.Name);
        Assert.Same(hotkey, candidateMacro.Hotkey);
        Assert.Same(sequence, candidateMacro.Sequence);
    }

    [Theory]
    [InlineData(MissingDependency.Macro)]
    [InlineData(MissingDependency.Hotkey)]
    [InlineData(MissingDependency.Sequence)]
    public void MissingDependency_ReportsOnlyPrerequisiteError(
        MissingDependency missing)
    {
        ApplicationSettingsCandidate candidate;
        (ConfigurationValidationErrorCode Code, string Path) expected;

        switch (missing)
        {
            case MissingDependency.Macro:
                candidate = new ApplicationSettingsCandidate(
                    new MacroDefinitionCandidate?[] { null });
                expected = (
                    ConfigurationValidationErrorCode.MacroRequired,
                    "macros[0]");
                break;

            case MissingDependency.Hotkey:
                candidate = new ApplicationSettingsCandidate(
                    new MacroDefinitionCandidate?[]
                    {
                        new(
                            SingleMacroCompatibility.LegacyMacroId,
                            "Valid Macro",
                            true,
                            null,
                            ValidSequence())
                    });
                expected = (
                    ConfigurationValidationErrorCode.HotkeyRequired,
                    "macros[0].hotkey");
                break;

            case MissingDependency.Sequence:
                candidate = new ApplicationSettingsCandidate(
                    new MacroDefinitionCandidate?[]
                    {
                        new(
                            SingleMacroCompatibility.LegacyMacroId,
                            "Valid Macro",
                            true,
                            ValidHotkey(),
                            null)
                    });
                expected = (
                    ConfigurationValidationErrorCode.MacroSequenceRequired,
                    "macros[0].steps");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(missing));
        }

        AssertInvalid(candidate, expected);
    }

    [Theory]
    [MemberData(nameof(RequiredNameCases))]
    public void Name_NullEmptyOrWhitespaceIsRequired(string? name)
    {
        AssertInvalid(
            CreateCandidate(name),
            (ConfigurationValidationErrorCode.MacroNameRequired, "macros[0].name"));
    }

    [Theory]
    [MemberData(nameof(NameLengthCases))]
    public void Name_UsesTrimmedLengthBoundaries(string rawName, bool isValid)
    {
        ApplicationSettingsCandidate candidate = CreateCandidate(rawName);
        ConfigurationValidationResult result =
            ConfigurationValidator.Validate(candidate);

        if (isValid)
        {
            ApplicationSettings settings = AssertValid(result);
            Assert.Equal(
                rawName.Trim(),
                Assert.Single(settings.Macros).Name);
            Assert.Equal(
                rawName,
                Assert.Single(candidate.Macros!)!.Name);
        }
        else
        {
            AssertInvalid(
                result,
                (ConfigurationValidationErrorCode.MacroNameTooLong, "macros[0].name"));
        }
    }

    [Fact]
    public void Name_EmbeddedControlCharacterIsRejected()
    {
        AssertInvalid(
            CreateCandidate("Valid\u0007Name"),
            (
                ConfigurationValidationErrorCode.MacroNameContainsControlCharacter,
                "macros[0].name"));
    }

    [Fact]
    public void IndependentErrors_AggregateInDeterministicCodeAndPathOrder()
    {
        string invalidName =
            new string('N', 32) + "\n" + new string('N', 33);
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep((KeyboardKey)int.MaxValue),
                new DelayStep(TimeSpan.Zero),
                new UnsupportedTestStep()
            });
        ApplicationSettingsCandidate candidate = CreateCandidate(
            invalidName,
            new KeyboardHotkey(HotkeyKey.A),
            sequence);

        AssertInvalid(
            candidate,
            (ConfigurationValidationErrorCode.MacroNameTooLong, "macros[0].name"),
            (
                ConfigurationValidationErrorCode.MacroNameContainsControlCharacter,
                "macros[0].name"),
            (
                ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
                "macros[0].hotkey"),
            (
                ConfigurationValidationErrorCode.UnsupportedOutputKey,
                "macros[0].steps[0].key"),
            (
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[0].steps[1].duration"),
            (
                ConfigurationValidationErrorCode.UnsupportedMacroStep,
                "macros[0].steps[2]"));
    }

    [Theory]
    [MemberData(nameof(LetterHotkeys))]
    public void ModifierlessLetter_AThroughZIsRejected(HotkeyKey key)
    {
        AssertInvalid(
            CreateCandidate(hotkey: new KeyboardHotkey(key)),
            (
                ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
                "macros[0].hotkey"));
    }

    [Theory]
    [MemberData(nameof(FunctionHotkeys))]
    public void ModifierlessFunctionKey_F1ThroughF11IsAccepted(HotkeyKey key)
    {
        AssertValid(
            ConfigurationValidator.Validate(
                CreateCandidate(hotkey: new KeyboardHotkey(key))));
    }

    [Theory]
    [InlineData(HotkeyKey.A, HotkeyModifiers.Control)]
    [InlineData(HotkeyKey.M, HotkeyModifiers.Alt)]
    [InlineData(HotkeyKey.Z, HotkeyModifiers.Shift)]
    [InlineData(
        HotkeyKey.C,
        HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    [InlineData(
        HotkeyKey.X,
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    public void ModifiedLetter_IsAccepted(
        HotkeyKey key,
        HotkeyModifiers modifiers)
    {
        AssertValid(
            ConfigurationValidator.Validate(
                CreateCandidate(
                    hotkey: new KeyboardHotkey(key, modifiers))));
    }

    [Theory]
    [InlineData(HotkeyKey.F1, HotkeyModifiers.Control)]
    [InlineData(
        HotkeyKey.F8,
        HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    [InlineData(
        HotkeyKey.F11,
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    public void ModifiedFunctionKey_IsAccepted(
        HotkeyKey key,
        HotkeyModifiers modifiers)
    {
        AssertValid(
            ConfigurationValidator.Validate(
                CreateCandidate(
                    hotkey: new KeyboardHotkey(key, modifiers))));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void SequenceStepCount_EnforcesBoundaries(int stepCount, bool isValid)
    {
        var steps = Enumerable
            .Range(0, stepCount)
            .Select(_ => (MacroStep)new KeyTapStep(KeyboardKey.A));
        var sequence = new MacroSequence(steps);
        ApplicationSettingsCandidate candidate = CreateCandidate(sequence: sequence);

        if (isValid)
        {
            AssertValid(ConfigurationValidator.Validate(candidate));
        }
        else if (stepCount == 0)
        {
            AssertInvalid(
                candidate,
                (
                    ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
                    "macros[0].steps"),
                (
                    ConfigurationValidationErrorCode.MacroMustContainKeyTap,
                    "macros[0].steps"));
        }
        else
        {
            AssertInvalid(
                candidate,
                (
                    ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
                    "macros[0].steps"));
        }
    }

    [Fact]
    public void DelayOnlySequence_RequiresKeyTap()
    {
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new DelayStep(TimeSpan.FromMilliseconds(1))
            });

        AssertInvalid(
            CreateCandidate(sequence: sequence),
            (
                ConfigurationValidationErrorCode.MacroMustContainKeyTap,
                "macros[0].steps"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60_000, true)]
    [InlineData(60_001, false)]
    public void IndividualDelay_EnforcesBoundaries(
        int milliseconds,
        bool isValid)
    {
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.A),
                new DelayStep(TimeSpan.FromMilliseconds(milliseconds))
            });
        ApplicationSettingsCandidate candidate = CreateCandidate(sequence: sequence);

        if (isValid)
        {
            AssertValid(ConfigurationValidator.Validate(candidate));
        }
        else
        {
            AssertInvalid(
                candidate,
                (
                    ConfigurationValidationErrorCode.DelayOutOfRange,
                    "macros[0].steps[1].duration"));
        }
    }

    [Theory]
    [InlineData(300_000, true)]
    [InlineData(300_001, false)]
    public void TotalDelay_EnforcesAggregateBoundary(
        int totalMilliseconds,
        bool isValid)
    {
        var steps = new List<MacroStep>
        {
            new KeyTapStep(KeyboardKey.A)
        };
        int remaining = totalMilliseconds;

        while (remaining > 0)
        {
            int nextDelay = Math.Min(remaining, 60_000);
            steps.Add(new DelayStep(TimeSpan.FromMilliseconds(nextDelay)));
            remaining -= nextDelay;
        }

        ApplicationSettingsCandidate candidate =
            CreateCandidate(sequence: new MacroSequence(steps));

        if (isValid)
        {
            AssertValid(ConfigurationValidator.Validate(candidate));
        }
        else
        {
            AssertInvalid(
                candidate,
                (
                    ConfigurationValidationErrorCode.TotalDelayTooLong,
                    "macros[0].steps"));
        }
    }

    [Fact]
    public void PathologicalDelay_CannotOverflowIntoApparentlyValidTotal()
    {
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.A),
                new DelayStep(TimeSpan.MaxValue)
            });

        AssertInvalid(
            CreateCandidate(sequence: sequence),
            (
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[0].steps[1].duration"),
            (
                ConfigurationValidationErrorCode.TotalDelayTooLong,
                "macros[0].steps"));
    }

    [Fact]
    public void UnsupportedMacroStep_IsReportedAndCountsAsOneRawStep()
    {
        var sequence = new MacroSequence(
            new MacroStep[] { new UnsupportedTestStep() });

        AssertInvalid(
            CreateCandidate(sequence: sequence),
            (
                ConfigurationValidationErrorCode.UnsupportedMacroStep,
                "macros[0].steps[0]"),
            (
                ConfigurationValidationErrorCode.MacroMustContainKeyTap,
                "macros[0].steps"));
    }

    [Fact]
    public void UndefinedOutputKey_IsReportedWithoutException()
    {
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep((KeyboardKey)int.MaxValue)
            });

        AssertInvalid(
            CreateCandidate(sequence: sequence),
            (
                ConfigurationValidationErrorCode.UnsupportedOutputKey,
                "macros[0].steps[0].key"));
    }

    [Fact]
    public void UndefinedOutputKey_AggregatesIndependentDelayError()
    {
        var sequence = new MacroSequence(
            new MacroStep[]
            {
                new KeyTapStep((KeyboardKey)(-1)),
                new DelayStep(TimeSpan.Zero)
            });

        AssertInvalid(
            CreateCandidate(sequence: sequence),
            (
                ConfigurationValidationErrorCode.UnsupportedOutputKey,
                "macros[0].steps[0].key"),
            (
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[0].steps[1].duration"));
    }

    [Fact]
    public void Validate_NullCandidateThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => ConfigurationValidator.Validate(null!));

        Assert.Equal("candidate", exception.ParamName);
    }

    private static ApplicationSettingsCandidate CreateCandidate(
        string? name = "Valid Macro",
        KeyboardHotkey? hotkey = null,
        MacroSequence? sequence = null)
    {
        return new ApplicationSettingsCandidate(
            new MacroDefinitionCandidate?[]
            {
                new(
                    SingleMacroCompatibility.LegacyMacroId,
                    name,
                    true,
                    hotkey ?? ValidHotkey(),
                    sequence ?? ValidSequence())
            });
    }

    private static KeyboardHotkey ValidHotkey()
    {
        return new KeyboardHotkey(HotkeyKey.F8);
    }

    private static MacroSequence ValidSequence()
    {
        return new MacroSequence(
            new MacroStep[] { new KeyTapStep(KeyboardKey.A) });
    }

    private static ApplicationSettings AssertValid(
        ConfigurationValidationResult result)
    {
        Assert.True(result.IsValid);
        ApplicationSettings settings = Assert.IsType<ApplicationSettings>(
            result.Settings);
        Assert.Empty(result.Errors);
        return settings;
    }

    private static ConfigurationValidationResult AssertInvalid(
        ApplicationSettingsCandidate candidate,
        params (ConfigurationValidationErrorCode Code, string Path)[] expected)
    {
        return AssertInvalid(ConfigurationValidator.Validate(candidate), expected);
    }

    private static ConfigurationValidationResult AssertInvalid(
        ConfigurationValidationResult result,
        params (ConfigurationValidationErrorCode Code, string Path)[] expected)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Settings);
        Assert.NotEmpty(result.Errors);
        Assert.All(
            result.Errors,
            error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
        Assert.Equal(
            expected,
            result.Errors.Select(error => (error.Code, error.Path)));
        return result;
    }

    public enum MissingDependency
    {
        Macro,
        Hotkey,
        Sequence
    }

    private sealed record UnsupportedTestStep : MacroStep;
}
