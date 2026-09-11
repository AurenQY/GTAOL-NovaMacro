using System.Collections.ObjectModel;
using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MacroEditorDraftTests
{
    [Fact]
    public void FromStarter_MapsExactEditableStateAndBoundaryMetadata()
    {
        MacroEditorDraft draft = CreateDraft();

        Assert.Equal("ABC Test", draft.Name);
        Assert.Equal(HotkeyKey.F8, draft.HotkeyKey);
        Assert.False(draft.Control);
        Assert.False(draft.Alt);
        Assert.False(draft.Shift);
        Assert.Collection(
            draft.Steps,
            step => AssertKeyTap(step, 1, KeyboardKey.A),
            step => AssertDelay(step, 2, "150"),
            step => AssertKeyTap(step, 3, KeyboardKey.B),
            step => AssertDelay(step, 4, "150"),
            step => AssertKeyTap(step, 5, KeyboardKey.C));
        Assert.False(draft.Steps[0].CanMoveUp);
        Assert.True(draft.Steps[0].CanMoveDown);
        Assert.True(draft.Steps[^1].CanMoveUp);
        Assert.False(draft.Steps[^1].CanMoveDown);
    }

    [Fact]
    public void MutatingDraft_DoesNotMutateStarterConfiguration()
    {
        ApplicationSettings starter = StarterConfiguration.Default;
        MacroEditorDraft draft = MacroEditorDraft.From(starter);

        draft.Name = "Changed";
        draft.HotkeyKey = HotkeyKey.F9;
        draft.Control = true;
        draft.Steps[0].Key = KeyboardKey.Z;
        draft.Steps[1].MillisecondsText = "999";
        draft.MoveDown(draft.Steps[0]);
        draft.Remove(draft.Steps[^1]);
        draft.AddDelay();

        MacroDefinition starterMacro = AssertSingleMacro(starter);
        Assert.Equal("ABC Test", starterMacro.Name);
        Assert.Equal(new KeyboardHotkey(HotkeyKey.F8), starterMacro.Hotkey);
        Assert.Collection(
            starterMacro.Sequence.Steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.A), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.B), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.C), step));
    }

    [Fact]
    public void ApprovedEnumVocabularies_MatchMainWindowEditorSources()
    {
        HotkeyKey[] expectedHotkeys =
        {
            HotkeyKey.A, HotkeyKey.B, HotkeyKey.C, HotkeyKey.D, HotkeyKey.E,
            HotkeyKey.F, HotkeyKey.G, HotkeyKey.H, HotkeyKey.I, HotkeyKey.J,
            HotkeyKey.K, HotkeyKey.L, HotkeyKey.M, HotkeyKey.N, HotkeyKey.O,
            HotkeyKey.P, HotkeyKey.Q, HotkeyKey.R, HotkeyKey.S, HotkeyKey.T,
            HotkeyKey.U, HotkeyKey.V, HotkeyKey.W, HotkeyKey.X, HotkeyKey.Y,
            HotkeyKey.Z,
            HotkeyKey.F1, HotkeyKey.F2, HotkeyKey.F3, HotkeyKey.F4,
            HotkeyKey.F5, HotkeyKey.F6, HotkeyKey.F7, HotkeyKey.F8,
            HotkeyKey.F9, HotkeyKey.F10, HotkeyKey.F11
        };
        KeyboardKey[] expectedOutputs =
        {
            KeyboardKey.A, KeyboardKey.B, KeyboardKey.C, KeyboardKey.D,
            KeyboardKey.E, KeyboardKey.F, KeyboardKey.G, KeyboardKey.H,
            KeyboardKey.I, KeyboardKey.J, KeyboardKey.K, KeyboardKey.L,
            KeyboardKey.M, KeyboardKey.N, KeyboardKey.O, KeyboardKey.P,
            KeyboardKey.Q, KeyboardKey.R, KeyboardKey.S, KeyboardKey.T,
            KeyboardKey.U, KeyboardKey.V, KeyboardKey.W, KeyboardKey.X,
            KeyboardKey.Y, KeyboardKey.Z,
            KeyboardKey.Digit0, KeyboardKey.Digit1, KeyboardKey.Digit2,
            KeyboardKey.Digit3, KeyboardKey.Digit4, KeyboardKey.Digit5,
            KeyboardKey.Digit6, KeyboardKey.Digit7, KeyboardKey.Digit8,
            KeyboardKey.Digit9,
            KeyboardKey.ArrowUp, KeyboardKey.ArrowDown,
            KeyboardKey.ArrowLeft, KeyboardKey.ArrowRight,
            KeyboardKey.Enter, KeyboardKey.Escape, KeyboardKey.Space,
            KeyboardKey.Tab, KeyboardKey.Backspace
        };

        Assert.Equal(37, expectedHotkeys.Length);
        Assert.Equal(expectedHotkeys, Enum.GetValues<HotkeyKey>());
        Assert.Equal(45, expectedOutputs.Length);
        Assert.Equal(expectedOutputs, Enum.GetValues<KeyboardKey>());
    }

    [Fact]
    public void ModifierlessLetter_CandidateReachesCfgAndIsInvalid()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.HotkeyKey = HotkeyKey.A;

        AssertInvalid(
            draft,
            ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
            "macros[0].hotkey");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void LetterWithAnySingleModifier_IsValid(
        bool control,
        bool alt,
        bool shift)
    {
        MacroEditorDraft draft = CreateDraft();
        draft.HotkeyKey = HotkeyKey.A;
        draft.Control = control;
        draft.Alt = alt;
        draft.Shift = shift;

        ApplicationSettings settings = AssertValid(draft);

        HotkeyModifiers expected = control
            ? HotkeyModifiers.Control
            : alt
                ? HotkeyModifiers.Alt
                : HotkeyModifiers.Shift;
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.A, expected),
            AssertSingleMacro(settings).Hotkey);
    }

    [Theory]
    [InlineData(HotkeyKey.F1)]
    [InlineData(HotkeyKey.F8)]
    [InlineData(HotkeyKey.F11)]
    public void ModifierlessRepresentativeFunctionKeys_AreValid(HotkeyKey key)
    {
        MacroEditorDraft draft = CreateDraft();
        draft.HotkeyKey = key;

        ApplicationSettings settings = AssertValid(draft);

        Assert.Equal(new KeyboardHotkey(key), AssertSingleMacro(settings).Hotkey);
    }

    [Fact]
    public void Name_NormalEditableValueIsValidatedAndTrimmed()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Name = "  My Macro  ";

        ApplicationSettings settings = AssertValid(draft);

        Assert.Equal("My Macro", AssertSingleMacro(settings).Name);
    }

    [Fact]
    public void Name_WhitespaceOnlyReachesCfgAndIsInvalid()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Name = "   ";

        AssertInvalid(
            draft,
            ConfigurationValidationErrorCode.MacroNameRequired,
            "macros[0].name");
    }

    [Fact]
    public void Name_Over64CharactersReachesCfgAndIsInvalid()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Name = new string('N', 65);

        AssertInvalid(
            draft,
            ConfigurationValidationErrorCode.MacroNameTooLong,
            "macros[0].name");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("150", 150)]
    [InlineData("60000", 60000)]
    public void Delay_ValidWholeMillisecondsParseAndValidate(
        string text,
        int expectedMilliseconds)
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = text;

        ApplicationSettingsCandidate candidate = AssertCandidate(draft);
        DelayStep delay = Assert.IsType<DelayStep>(
            AssertSingleMacro(candidate).Sequence!.Steps[1]);
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            delay.Duration);
        Assert.True(ConfigurationValidator.Validate(candidate).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("922337203685478")]
    public void Delay_UnrepresentableTextReturnsReadableParseError(string text)
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = text;

        bool created = draft.TryCreateCandidate(
            out ApplicationSettingsCandidate? candidate,
            out IReadOnlyList<EditorParseError> errors);

        Assert.False(created);
        Assert.Null(candidate);
        EditorParseError error = Assert.Single(errors);
        Assert.Equal("macros[0].steps[1].milliseconds", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("60001", 60001)]
    public void Delay_RepresentableOutOfRangeValueReachesCfg(
        string text,
        int expectedMilliseconds)
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = text;

        ApplicationSettingsCandidate candidate = AssertCandidate(draft);
        DelayStep delay = Assert.IsType<DelayStep>(
            AssertSingleMacro(candidate).Sequence!.Steps[1]);
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            delay.Duration);

        AssertValidationError(
            ConfigurationValidator.Validate(candidate),
            ConfigurationValidationErrorCode.DelayOutOfRange,
            "macros[0].steps[1].duration");
    }

    [Fact]
    public void MultipleMalformedDelays_ReturnErrorsForBothRowsInOrder()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = "abc";
        draft.Steps[3].MillisecondsText = "1.5";

        bool created = draft.TryCreateCandidate(
            out ApplicationSettingsCandidate? candidate,
            out IReadOnlyList<EditorParseError> errors);

        Assert.False(created);
        Assert.Null(candidate);
        Assert.Equal(
            new[]
            {
                "macros[0].steps[1].milliseconds",
                "macros[0].steps[3].milliseconds"
            },
            errors.Select(error => error.Path));
        Assert.All(
            errors,
            error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }

    [Fact]
    public void ParseError_CanRecoverToValidAfterCorrection()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = "abc";

        Assert.False(draft.TryCreateCandidate(out var invalidCandidate, out var errors));
        Assert.Null(invalidCandidate);
        Assert.NotEmpty(errors);

        draft.Steps[1].MillisecondsText = "150";

        AssertValid(draft);
    }

    [Fact]
    public void SemanticError_CanRecoverToValidAfterModifierChange()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.HotkeyKey = HotkeyKey.A;
        AssertInvalid(
            draft,
            ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
            "macros[0].hotkey");

        draft.Control = true;

        AssertValid(draft);
    }

    [Fact]
    public void AddKeyTap_AppendsDefaultAndRefreshesBoundaries()
    {
        MacroEditorDraft draft = CreateDraft();
        MacroStepEditorDraft previousLast = draft.Steps[^1];

        draft.AddKeyTap();

        MacroStepEditorDraft added = draft.Steps[^1];
        AssertKeyTap(added, 6, KeyboardKey.A);
        Assert.True(previousLast.CanMoveDown);
        Assert.True(added.CanMoveUp);
        Assert.False(added.CanMoveDown);
        AssertContiguousPositions(draft);
    }

    [Fact]
    public void AddDelay_AppendsDefaultAndRefreshesBoundaries()
    {
        MacroEditorDraft draft = CreateDraft();
        MacroStepEditorDraft previousLast = draft.Steps[^1];

        draft.AddDelay();

        MacroStepEditorDraft added = draft.Steps[^1];
        AssertDelay(added, 6, "150");
        Assert.True(previousLast.CanMoveDown);
        Assert.True(added.CanMoveUp);
        Assert.False(added.CanMoveDown);
        AssertContiguousPositions(draft);
    }

    [Theory]
    [InlineData(0, "D:150,K:B,D:150,K:C")]
    [InlineData(2, "K:A,D:150,D:150,K:C")]
    [InlineData(4, "K:A,D:150,K:B,D:150")]
    public void Remove_FirstMiddleOrLastRenumbersAndPreservesOrder(
        int index,
        string expectedOrder)
    {
        MacroEditorDraft draft = CreateDraft();

        draft.Remove(draft.Steps[index]);

        Assert.Equal(expectedOrder, DescribeSequence(draft));
        AssertContiguousPositions(draft);
        Assert.False(draft.Steps[0].CanMoveUp);
        Assert.False(draft.Steps[^1].CanMoveDown);
    }

    [Fact]
    public void RemoveAllSteps_IsRepresentableAndCfgInvalid()
    {
        MacroEditorDraft draft = CreateDraft();
        foreach (MacroStepEditorDraft step in draft.Steps.ToArray())
        {
            draft.Remove(step);
        }

        Assert.Empty(draft.Steps);
        ApplicationSettingsCandidate candidate = AssertCandidate(draft);
        ConfigurationValidationResult validation =
            ConfigurationValidator.Validate(candidate);
        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            error => error.Code ==
                ConfigurationValidationErrorCode.MacroStepCountOutOfRange);
        Assert.Contains(
            validation.Errors,
            error => error.Code ==
                ConfigurationValidationErrorCode.MacroMustContainKeyTap);
    }

    [Fact]
    public void RemoveAllKeyTaps_LeavesDelaysAndCfgRejectsMissingKeyTap()
    {
        MacroEditorDraft draft = CreateDraft();
        foreach (MacroStepEditorDraft step in draft.Steps
            .Where(step => step.IsKeyTap)
            .ToArray())
        {
            draft.Remove(step);
        }

        Assert.Equal("D:150,D:150", DescribeSequence(draft));
        AssertInvalid(
            draft,
            ConfigurationValidationErrorCode.MacroMustContainKeyTap,
            "macros[0].steps");
    }

    [Fact]
    public void MoveUp_ChangesOrderAndRefreshesMetadata()
    {
        MacroEditorDraft draft = CreateDraft();
        MacroStepEditorDraft step = draft.Steps[2];

        draft.MoveUp(step);

        Assert.Equal("K:A,K:B,D:150,D:150,K:C", DescribeSequence(draft));
        Assert.Equal(2, step.Position);
        Assert.True(step.CanMoveUp);
        Assert.True(step.CanMoveDown);
        AssertContiguousPositions(draft);
    }

    [Fact]
    public void MoveDown_ChangesOrderAndRefreshesMetadata()
    {
        MacroEditorDraft draft = CreateDraft();
        MacroStepEditorDraft step = draft.Steps[1];

        draft.MoveDown(step);

        Assert.Equal("K:A,K:B,D:150,D:150,K:C", DescribeSequence(draft));
        Assert.Equal(3, step.Position);
        Assert.True(step.CanMoveUp);
        Assert.True(step.CanMoveDown);
        AssertContiguousPositions(draft);
    }

    [Fact]
    public void MoveAtBoundaries_IsSafeNoOp()
    {
        MacroEditorDraft draft = CreateDraft();
        string original = DescribeSequence(draft);

        draft.MoveUp(draft.Steps[0]);
        draft.MoveDown(draft.Steps[^1]);

        Assert.Equal(original, DescribeSequence(draft));
        Assert.False(draft.Steps[0].CanMoveUp);
        Assert.False(draft.Steps[^1].CanMoveDown);
        AssertContiguousPositions(draft);
    }

    [Fact]
    public void ReorderedDraft_CandidatePreservesExactOrderAndRemainsValid()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = "100";
        draft.Steps[3].MillisecondsText = "200";
        MacroStepEditorDraft finalKeyTap = draft.Steps[4];
        draft.MoveUp(finalKeyTap);
        draft.MoveUp(finalKeyTap);

        ApplicationSettings settings = AssertValid(draft);

        Assert.Collection(
            AssertSingleMacro(settings).Sequence.Steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.A), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(100)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.C), step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.B), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(200)),
                step));
        AssertContiguousPositions(draft);
    }

    [Fact]
    public void PositionsRemainContiguousAfterAddRemoveAndMove()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.AddDelay();
        draft.Remove(draft.Steps[1]);
        draft.MoveUp(draft.Steps[^1]);
        draft.AddKeyTap();
        draft.MoveDown(draft.Steps[0]);

        AssertContiguousPositions(draft);
        Assert.False(draft.Steps[0].CanMoveUp);
        Assert.False(draft.Steps[^1].CanMoveDown);
    }

    [Fact]
    public void CandidateConversion_IgnoresPropertiesIrrelevantToStepKind()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[0].MillisecondsText = "not a delay";
        draft.Steps[1].Key = (KeyboardKey)int.MaxValue;

        ApplicationSettingsCandidate candidate = AssertCandidate(draft);

        Assert.Equal(
            new KeyTapStep(KeyboardKey.A),
            AssertSingleMacro(candidate).Sequence!.Steps[0]);
        Assert.Equal(
            new DelayStep(TimeSpan.FromMilliseconds(150)),
            AssertSingleMacro(candidate).Sequence!.Steps[1]);
        Assert.True(ConfigurationValidator.Validate(candidate).IsValid);
    }

    [Fact]
    public void OneHundredOneSteps_AreRepresentableAndCfgRejectsCount()
    {
        MacroEditorDraft draft = CreateDraft();
        while (draft.Steps.Count < 101)
        {
            draft.AddKeyTap();
        }

        ApplicationSettingsCandidate candidate = AssertCandidate(draft);
        Assert.Equal(101, AssertSingleMacro(candidate).Sequence!.Steps.Count);
        AssertValidationError(
            ConfigurationValidator.Validate(candidate),
            ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
            "macros[0].steps");
    }

    [Fact]
    public void TotalDelayOverLimit_IsRepresentableAndCfgRejectsTotal()
    {
        MacroEditorDraft draft = CreateDraft();
        foreach (MacroStepEditorDraft step in draft.Steps.ToArray())
        {
            draft.Remove(step);
        }

        draft.AddKeyTap();
        for (int index = 0; index < 6; index++)
        {
            draft.AddDelay();
            draft.Steps[^1].MillisecondsText = "60000";
        }

        ApplicationSettingsCandidate candidate = AssertCandidate(draft);
        AssertValidationError(
            ConfigurationValidator.Validate(candidate),
            ConfigurationValidationErrorCode.TotalDelayTooLong,
            "macros[0].steps");
    }

    [Fact]
    public void StepsCollection_IsReadOnlyToConsumers()
    {
        MacroEditorDraft draft = CreateDraft();
        ReadOnlyObservableCollection<MacroStepEditorDraft> steps = draft.Steps;
        IList<MacroStepEditorDraft> list = steps;

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Equal(5, draft.Steps.Count);
    }

    private static MacroEditorDraft CreateDraft()
    {
        return MacroEditorDraft.From(StarterConfiguration.Default);
    }

    private static ApplicationSettingsCandidate AssertCandidate(
        MacroEditorDraft draft)
    {
        bool created = draft.TryCreateCandidate(
            out ApplicationSettingsCandidate? candidate,
            out IReadOnlyList<EditorParseError> errors);

        Assert.True(created);
        Assert.Empty(errors);
        return Assert.IsType<ApplicationSettingsCandidate>(candidate);
    }

    private static ApplicationSettings AssertValid(MacroEditorDraft draft)
    {
        ConfigurationValidationResult validation =
            ConfigurationValidator.Validate(AssertCandidate(draft));

        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        return Assert.IsType<ApplicationSettings>(validation.Settings);
    }

    private static MacroDefinition AssertSingleMacro(
        ApplicationSettings settings)
    {
        return Assert.Single(settings.Macros);
    }

    private static MacroDefinitionCandidate AssertSingleMacro(
        ApplicationSettingsCandidate candidate)
    {
        return Assert.Single(candidate.Macros!)!;
    }

    private static void AssertInvalid(
        MacroEditorDraft draft,
        ConfigurationValidationErrorCode expectedCode,
        string expectedPath)
    {
        AssertValidationError(
            ConfigurationValidator.Validate(AssertCandidate(draft)),
            expectedCode,
            expectedPath);
    }

    private static void AssertValidationError(
        ConfigurationValidationResult validation,
        ConfigurationValidationErrorCode expectedCode,
        string expectedPath)
    {
        Assert.False(validation.IsValid);
        Assert.Null(validation.Settings);
        ConfigurationValidationError error = Assert.Single(validation.Errors);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedPath, error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static void AssertKeyTap(
        MacroStepEditorDraft step,
        int position,
        KeyboardKey key)
    {
        Assert.Equal(MacroStepEditorKind.KeyTap, step.Kind);
        Assert.True(step.IsKeyTap);
        Assert.False(step.IsDelay);
        Assert.Equal("Key Tap", step.TypeLabel);
        Assert.Equal(position, step.Position);
        Assert.Equal(key, step.Key);
    }

    private static void AssertDelay(
        MacroStepEditorDraft step,
        int position,
        string millisecondsText)
    {
        Assert.Equal(MacroStepEditorKind.Delay, step.Kind);
        Assert.False(step.IsKeyTap);
        Assert.True(step.IsDelay);
        Assert.Equal("Delay", step.TypeLabel);
        Assert.Equal(position, step.Position);
        Assert.Equal(millisecondsText, step.MillisecondsText);
    }

    private static void AssertContiguousPositions(MacroEditorDraft draft)
    {
        Assert.Equal(
            Enumerable.Range(1, draft.Steps.Count),
            draft.Steps.Select(step => step.Position));
    }

    private static string DescribeSequence(MacroEditorDraft draft)
    {
        return string.Join(
            ",",
            draft.Steps.Select(step => step.IsKeyTap
                ? $"K:{step.Key}"
                : $"D:{step.MillisecondsText}"));
    }
}
