using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MacroEditorSaveCoordinatorTests
{
    [Fact]
    public void Constructor_NullStoreThrows()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new MacroEditorSaveCoordinator(null!));

        Assert.Equal("settingsStore", exception.ParamName);
    }

    [Fact]
    public void SettingsPath_DelegatesExactStorePath()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);

        Assert.Equal(store.SettingsPath, coordinator.SettingsPath);
        Assert.Equal(0, store.LoadCallCount);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public void StarterDraft_ValidatesAndSavesValidatedSettingsExactlyOnce()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();

        MacroEditorValidationResult validation = coordinator.Validate(draft);
        MacroEditorSaveResult result = coordinator.Save(draft);

        AssertValidValidation(validation);
        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        AssertValidValidation(result.Validation);
        Assert.Null(result.StorageError);
        Assert.Equal(1, store.SaveCallCount);
        ApplicationSettings saved = Assert.Single(store.SavedSettings);
        MacroDefinition savedMacro = Assert.Single(saved.Macros);
        Assert.Same(result.Validation.Settings, saved);
        Assert.Equal("ABC Test", savedMacro.Name);
        Assert.Equal(new KeyboardHotkey(HotkeyKey.F8), savedMacro.Hotkey);
        Assert.Equal(0, store.LoadCallCount);
    }

    [Fact]
    public void Save_ParseInvalidDraftReturnsInvalidAndNeverCallsStore()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = "abc";

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Invalid, result.Status);
        Assert.Null(result.StorageError);
        Assert.False(result.Validation.IsValid);
        Assert.Null(result.Validation.Settings);
        EditorParseError error = Assert.Single(result.Validation.ParseErrors);
        Assert.Equal("macros[0].steps[1].milliseconds", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Empty(result.Validation.ConfigurationErrors);
        Assert.Empty(store.SavedSettings);
        Assert.Equal(0, store.SaveCallCount);
        Assert.Equal(0, store.LoadCallCount);
    }

    [Fact]
    public void Save_SemanticInvalidDraftReturnsInvalidAndNeverCallsStore()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.HotkeyKey = HotkeyKey.A;

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Invalid, result.Status);
        Assert.Null(result.StorageError);
        Assert.False(result.Validation.IsValid);
        Assert.Null(result.Validation.Settings);
        Assert.Empty(result.Validation.ParseErrors);
        ConfigurationValidationError error = Assert.Single(
            result.Validation.ConfigurationErrors);
        Assert.Equal(
            ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
            error.Code);
        Assert.Equal("macros[0].hotkey", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Empty(store.SavedSettings);
        Assert.Equal(0, store.SaveCallCount);
        Assert.Equal(0, store.LoadCallCount);
    }

    [Fact]
    public void Save_RevalidatesValidInvalidValidDraftOnEveryAttempt()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();

        MacroEditorSaveResult first = coordinator.Save(draft);
        draft.Steps[1].MillisecondsText = "abc";
        MacroEditorSaveResult invalid = coordinator.Save(draft);
        draft.Steps[1].MillisecondsText = "150";
        MacroEditorSaveResult recovered = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, first.Status);
        Assert.Equal(MacroEditorSaveStatus.Invalid, invalid.Status);
        Assert.NotEmpty(invalid.Validation.ParseErrors);
        Assert.Equal(MacroEditorSaveStatus.Saved, recovered.Status);
        AssertValidValidation(recovered.Validation);
        Assert.Equal(2, store.SaveCallCount);
        Assert.Equal(2, store.SavedSettings.Count);
        Assert.Equal(0, store.LoadCallCount);
    }

    [Fact]
    public void Save_PersistsNormalizedNameWithoutRewritingRawDraft()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.Name = "  My Macro  ";

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        Assert.Equal(
            "My Macro",
            Assert.Single(Assert.Single(store.SavedSettings).Macros).Name);
        Assert.Equal("  My Macro  ", draft.Name);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public void Save_EditedDraftPreservesLegacyIdentityAndEnabledState()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.Name = "Edited Legacy Macro";
        draft.HotkeyKey = HotkeyKey.F9;
        draft.Control = true;
        draft.Steps[0].Key = KeyboardKey.M;

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        MacroDefinition savedMacro = Assert.Single(
            Assert.Single(store.SavedSettings).Macros);
        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, savedMacro.Id);
        Assert.True(savedMacro.Enabled);
        Assert.Equal("Edited Legacy Macro", savedMacro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F9, HotkeyModifiers.Control),
            savedMacro.Hotkey);
        Assert.Equal(
            new KeyTapStep(KeyboardKey.M),
            savedMacro.Sequence.Steps[0]);
    }

    [Fact]
    public void Save_PersistsEditedHotkeyAndMultipleModifiersExactly()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.HotkeyKey = HotkeyKey.F9;
        draft.Control = true;
        draft.Alt = true;
        draft.Shift = false;

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        Assert.Equal(
            new KeyboardHotkey(
                HotkeyKey.F9,
                HotkeyModifiers.Control | HotkeyModifiers.Alt),
            Assert.Single(Assert.Single(store.SavedSettings).Macros).Hotkey);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public void Save_PersistsExactEditedSequenceOrder()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[0].Key = KeyboardKey.M;
        draft.Steps[1].MillisecondsText = "250";
        draft.Steps[2].Key = KeyboardKey.Enter;
        draft.Steps[3].MillisecondsText = "10";
        draft.Steps[4].Key = KeyboardKey.ArrowUp;

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        Assert.Collection(
            Assert.Single(Assert.Single(store.SavedSettings).Macros)
                .Sequence.Steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.M), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(250)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.Enter), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(10)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.ArrowUp), step));
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public void Save_PersistsParsedDelayValueRatherThanRawText()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();
        draft.Steps[1].MillisecondsText = "00150";

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        DelayStep delay = Assert.IsType<DelayStep>(
            Assert.Single(Assert.Single(store.SavedSettings).Macros)
                .Sequence.Steps[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(150), delay.Duration);
        Assert.Equal("00150", draft.Steps[1].MillisecondsText);
    }

    [Fact]
    public void Save_StoreFailureMapsToStorageFailedAndPreservesExactError()
    {
        var store = new RecordingSettingsStore();
        var expectedError = new SettingsStorageError(
            SettingsStorageErrorCode.PublishFailed,
            store.SettingsPath,
            "Publication failed for the test.");
        store.SaveResult = SettingsSaveResult.Failure(expectedError);
        var coordinator = new MacroEditorSaveCoordinator(store);

        MacroEditorSaveResult result = coordinator.Save(CreateDraft());

        Assert.Equal(MacroEditorSaveStatus.StorageFailed, result.Status);
        AssertValidValidation(result.Validation);
        Assert.Same(expectedError, result.StorageError);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Single(store.SavedSettings);
        Assert.Equal(0, store.LoadCallCount);
    }

    [Fact]
    public void Save_SuccessDoesNotMutateDraft()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateEditedDraft();
        string before = Snapshot(draft);

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, result.Status);
        Assert.Equal(before, Snapshot(draft));
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public void Save_FailureDoesNotMutateDraft()
    {
        var store = new RecordingSettingsStore();
        store.SaveResult = SettingsSaveResult.Failure(
            new SettingsStorageError(
                SettingsStorageErrorCode.TemporaryFileWriteFailed,
                store.SettingsPath,
                "Temporary write failed for the test."));
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateEditedDraft();
        string before = Snapshot(draft);

        MacroEditorSaveResult result = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.StorageFailed, result.Status);
        Assert.Equal(before, Snapshot(draft));
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public void Save_RepeatedValidAttemptsEachCallStoreExactlyOnce()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();

        MacroEditorSaveResult first = coordinator.Save(draft);
        MacroEditorSaveResult second = coordinator.Save(draft);

        Assert.Equal(MacroEditorSaveStatus.Saved, first.Status);
        Assert.Equal(MacroEditorSaveStatus.Saved, second.Status);
        Assert.Equal(2, store.SaveCallCount);
        Assert.Equal(2, store.SavedSettings.Count);
        Assert.Equal(0, store.LoadCallCount);
    }

    [Fact]
    public void CoordinatorFlows_NeverCallLoad()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new MacroEditorSaveCoordinator(store);
        MacroEditorDraft draft = CreateDraft();

        AssertValidValidation(coordinator.Validate(draft));
        Assert.Equal(MacroEditorSaveStatus.Saved, coordinator.Save(draft).Status);

        draft.Steps[1].MillisecondsText = "abc";
        Assert.False(coordinator.Validate(draft).IsValid);
        Assert.Equal(MacroEditorSaveStatus.Invalid, coordinator.Save(draft).Status);

        draft.Steps[1].MillisecondsText = "150";
        store.SaveResult = SettingsSaveResult.Failure(
            new SettingsStorageError(
                SettingsStorageErrorCode.PublishFailed,
                store.SettingsPath,
                "Publication failed for the test."));
        Assert.Equal(
            MacroEditorSaveStatus.StorageFailed,
            coordinator.Save(draft).Status);

        Assert.Equal(0, store.LoadCallCount);
        Assert.Equal(2, store.SaveCallCount);
    }

    private static MacroEditorDraft CreateDraft()
    {
        return MacroEditorDraft.From(StarterConfiguration.Default);
    }

    private static MacroEditorDraft CreateEditedDraft()
    {
        MacroEditorDraft draft = CreateDraft();
        draft.Name = "  Edited Macro  ";
        draft.HotkeyKey = HotkeyKey.F9;
        draft.Control = true;
        draft.Alt = true;
        draft.Steps[0].Key = KeyboardKey.M;
        draft.Steps[1].MillisecondsText = "00250";
        draft.MoveUp(draft.Steps[2]);
        draft.AddDelay();
        draft.Steps[^1].MillisecondsText = "10";
        return draft;
    }

    private static void AssertValidValidation(
        MacroEditorValidationResult validation)
    {
        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Settings);
        Assert.Empty(validation.ParseErrors);
        Assert.Empty(validation.ConfigurationErrors);
        Assert.Empty(validation.ErrorMessages);
    }

    private static string Snapshot(MacroEditorDraft draft)
    {
        string steps = string.Join(
            ";",
            draft.Steps.Select(step =>
                $"{step.Position}:{step.Kind}:{step.Key}:" +
                $"{step.MillisecondsText}:{step.CanMoveUp}:" +
                $"{step.CanMoveDown}"));

        return $"{draft.Name}|{draft.HotkeyKey}|{draft.Control}|" +
            $"{draft.Alt}|{draft.Shift}|{steps}";
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        internal RecordingSettingsStore()
        {
            SettingsPath = Path.Combine(
                Path.GetTempPath(),
                "NovaMacro.Tests",
                Guid.NewGuid().ToString("N"),
                "settings.json");
        }

        public string SettingsPath { get; }

        internal int LoadCallCount { get; private set; }

        internal int SaveCallCount { get; private set; }

        internal List<ApplicationSettings> SavedSettings { get; } = new();

        internal SettingsSaveResult SaveResult { get; set; } =
            SettingsSaveResult.Success();

        public SettingsLoadResult Load()
        {
            LoadCallCount++;
            return SettingsLoadResult.Missing();
        }

        public SettingsSaveResult Save(ApplicationSettings settings)
        {
            SaveCallCount++;
            SavedSettings.Add(settings);
            return SaveResult;
        }
    }
}
