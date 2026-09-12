using NovaMacro.App.Configuration.Storage;

namespace NovaMacro.App.Editor;

internal enum MacroEditorSaveStatus
{
    Saved,
    Invalid,
    StorageFailed
}

internal sealed class MacroEditorSaveResult
{
    private MacroEditorSaveResult(
        MacroEditorSaveStatus status,
        MacroEditorValidationResult validation,
        SettingsStorageError? storageError)
    {
        Status = status;
        Validation = validation;
        StorageError = storageError;
    }

    internal MacroEditorSaveStatus Status { get; }

    internal MacroEditorValidationResult Validation { get; }

    internal SettingsStorageError? StorageError { get; }

    internal static MacroEditorSaveResult Saved(
        MacroEditorValidationResult validation)
    {
        return new MacroEditorSaveResult(
            MacroEditorSaveStatus.Saved,
            validation,
            null);
    }

    internal static MacroEditorSaveResult Invalid(
        MacroEditorValidationResult validation)
    {
        return new MacroEditorSaveResult(
            MacroEditorSaveStatus.Invalid,
            validation,
            null);
    }

    internal static MacroEditorSaveResult StorageFailed(
        MacroEditorValidationResult validation,
        SettingsStorageError storageError)
    {
        ArgumentNullException.ThrowIfNull(storageError);

        return new MacroEditorSaveResult(
            MacroEditorSaveStatus.StorageFailed,
            validation,
            storageError);
    }
}

internal sealed class MacroEditorSaveCoordinator
{
    private readonly ISettingsStore _settingsStore;

    internal MacroEditorSaveCoordinator(ISettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        _settingsStore = settingsStore;
    }

    internal string SettingsPath => _settingsStore.SettingsPath;

    internal MacroEditorValidationResult Validate(MacroEditorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Validate();
    }

    internal MacroEditorValidationResult Validate(MultiMacroEditorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Validate();
    }

    internal MacroEditorSaveResult Save(MacroEditorDraft draft)
    {
        MacroEditorValidationResult validation = Validate(draft);

        if (!validation.IsValid)
        {
            return MacroEditorSaveResult.Invalid(validation);
        }

        SettingsSaveResult storageResult =
            _settingsStore.Save(validation.Settings!);

        return storageResult.IsSuccess
            ? MacroEditorSaveResult.Saved(validation)
            : MacroEditorSaveResult.StorageFailed(
                validation,
                storageResult.Error!);
    }

    internal MacroEditorSaveResult Save(MultiMacroEditorDraft draft)
    {
        MacroEditorValidationResult validation = Validate(draft);

        if (!validation.IsValid)
        {
            return MacroEditorSaveResult.Invalid(validation);
        }

        SettingsSaveResult storageResult =
            _settingsStore.Save(validation.Settings!);

        return storageResult.IsSuccess
            ? MacroEditorSaveResult.Saved(validation)
            : MacroEditorSaveResult.StorageFailed(
                validation,
                storageResult.Error!);
    }
}
