using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class StartupConfigurationCoordinatorTests
{
    [Fact]
    public void Constructor_NullStoreThrows()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new StartupConfigurationCoordinator(null!));

        Assert.Equal("settingsStore", exception.ParamName);
    }

    [Fact]
    public void Resolve_MissingUsesOneStarterSnapshotWithoutSaving()
    {
        var store = new RecordingSettingsStore();
        var coordinator = new StartupConfigurationCoordinator(store);

        StartupConfigurationResult result = coordinator.Resolve();

        Assert.Equal(StartupConfigurationStatus.StarterReady, result.Status);
        Assert.Same(StarterConfiguration.Default, result.InitialEditorSettings);
        Assert.Same(result.InitialEditorSettings, result.RuntimeSettings);
        Assert.Equal(store.SettingsPath, result.SettingsPath);
        Assert.Null(result.JsonReadResult);
        Assert.Null(result.StorageError);
        Assert.Empty(result.Diagnostics);
        AssertStarterSemantics(result.InitialEditorSettings);
        AssertStoreCalls(store);
    }

    [Fact]
    public void Resolve_LoadedUsesExactSavedSnapshotWithoutSaving()
    {
        ApplicationSettings loaded = CreateLoadedSettings();
        var store = new RecordingSettingsStore
        {
            LoadResult = SettingsLoadResult.Loaded(loaded)
        };
        var coordinator = new StartupConfigurationCoordinator(store);

        StartupConfigurationResult result = coordinator.Resolve();

        Assert.Equal(StartupConfigurationStatus.SavedReady, result.Status);
        Assert.Same(loaded, result.InitialEditorSettings);
        Assert.Same(loaded, result.RuntimeSettings);
        Assert.Same(result.InitialEditorSettings, result.RuntimeSettings);
        Assert.Equal(store.SettingsPath, result.SettingsPath);
        MacroDefinition runtimeMacro =
            Assert.Single(result.RuntimeSettings!.Macros);
        Assert.Equal("Loaded A", runtimeMacro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F9, HotkeyModifiers.Control),
            runtimeMacro.Hotkey);
        Assert.Collection(
            runtimeMacro.Sequence.Steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.M), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(250)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.Enter), step));
        Assert.Null(result.JsonReadResult);
        Assert.Null(result.StorageError);
        Assert.Empty(result.Diagnostics);
        AssertStoreCalls(store);
    }

    [Fact]
    public void Resolve_InvalidCodecRecoversEditorAndPreservesDiagnostic()
    {
        var error = new SettingsJsonError(
            SettingsJsonErrorCode.InvalidJson,
            "$",
            "Malformed JSON for the test.");
        SettingsJsonReadResult jsonResult =
            SettingsJsonReadResult.CodecFailure(error);
        var store = new RecordingSettingsStore
        {
            LoadResult = SettingsLoadResult.Invalid(jsonResult)
        };
        var coordinator = new StartupConfigurationCoordinator(store);

        StartupConfigurationResult result = coordinator.Resolve();

        Assert.Equal(
            StartupConfigurationStatus.InvalidRecovery,
            result.Status);
        AssertRecoveryStarter(result);
        Assert.Same(jsonResult, result.JsonReadResult);
        Assert.Same(error, Assert.Single(result.JsonReadResult!.CodecErrors));
        Assert.Empty(result.JsonReadResult.ValidationErrors);
        Assert.Null(result.StorageError);
        Assert.Equal(
            new[]
            {
                $"Settings path: {store.SettingsPath}",
                $"{error.Code} at {error.Path}: {error.Message}"
            },
            result.Diagnostics);
        AssertStoreCalls(store);
    }

    [Fact]
    public void Resolve_InvalidConfigurationPreservesOrderedDiagnostics()
    {
        ConfigurationValidationError[] errors =
        {
            new(
                ConfigurationValidationErrorCode
                    .ModifierlessLetterHotkeyNotAllowed,
                "macros[0].hotkey",
                "Modifierless letter hotkey for the test."),
            new(
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[0].steps[1].milliseconds",
                "Delay range failure for the test.")
        };
        SettingsJsonReadResult jsonResult =
            SettingsJsonReadResult.FromValidation(
                ConfigurationValidationResult.Invalid(errors));
        var store = new RecordingSettingsStore
        {
            LoadResult = SettingsLoadResult.Invalid(jsonResult)
        };
        var coordinator = new StartupConfigurationCoordinator(store);

        StartupConfigurationResult result = coordinator.Resolve();

        Assert.Equal(
            StartupConfigurationStatus.InvalidRecovery,
            result.Status);
        AssertRecoveryStarter(result);
        Assert.Same(jsonResult, result.JsonReadResult);
        Assert.Empty(result.JsonReadResult!.CodecErrors);
        Assert.Equal(errors, result.JsonReadResult.ValidationErrors);
        Assert.Null(result.StorageError);
        Assert.Equal(
            new[]
            {
                $"Settings path: {store.SettingsPath}",
                $"{errors[0].Code} at {errors[0].Path}: " +
                    errors[0].Message,
                $"{errors[1].Code} at {errors[1].Path}: " +
                    errors[1].Message
            },
            result.Diagnostics);
        AssertStoreCalls(store);
    }

    [Fact]
    public void Resolve_StorageInvalidRecoversEditorAndPreservesError()
    {
        var store = new RecordingSettingsStore();
        var error = new SettingsStorageError(
            SettingsStorageErrorCode.InvalidUtf8,
            store.SettingsPath,
            "Invalid UTF-8 for the test.");
        store.LoadResult = SettingsLoadResult.Invalid(error);
        var coordinator = new StartupConfigurationCoordinator(store);

        StartupConfigurationResult result = coordinator.Resolve();

        Assert.Equal(
            StartupConfigurationStatus.InvalidRecovery,
            result.Status);
        AssertRecoveryStarter(result);
        Assert.Null(result.JsonReadResult);
        Assert.Same(error, result.StorageError);
        Assert.Equal(
            new[]
            {
                $"Settings path: {store.SettingsPath}",
                $"{error.Code}: {error.Message}"
            },
            result.Diagnostics);
        AssertStoreCalls(store);
    }

    [Fact]
    public void Resolve_UnavailableRecoversEditorAndPreservesError()
    {
        var store = new RecordingSettingsStore();
        var error = new SettingsStorageError(
            SettingsStorageErrorCode.ReadFailed,
            store.SettingsPath,
            "Read failure for the test.");
        store.LoadResult = SettingsLoadResult.Unavailable(error);
        var coordinator = new StartupConfigurationCoordinator(store);

        StartupConfigurationResult result = coordinator.Resolve();

        Assert.Equal(
            StartupConfigurationStatus.UnavailableRecovery,
            result.Status);
        AssertRecoveryStarter(result);
        Assert.Null(result.JsonReadResult);
        Assert.Same(error, result.StorageError);
        Assert.Equal(
            new[]
            {
                $"Settings path: {store.SettingsPath}",
                $"{error.Code}: {error.Message}"
            },
            result.Diagnostics);
        AssertStoreCalls(store);
    }

    [Fact]
    public void Resolve_OnlyMissingProvidesStarterRuntime()
    {
        var missingStore = new RecordingSettingsStore();

        var invalidStore = new RecordingSettingsStore
        {
            LoadResult = SettingsLoadResult.Invalid(
                SettingsJsonReadResult.CodecFailure(
                    new SettingsJsonError(
                        SettingsJsonErrorCode.InvalidJson,
                        "$",
                        "Malformed JSON for the test.")))
        };

        var unavailableStore = new RecordingSettingsStore();
        unavailableStore.LoadResult = SettingsLoadResult.Unavailable(
            new SettingsStorageError(
                SettingsStorageErrorCode.ReadFailed,
                unavailableStore.SettingsPath,
                "Read failure for the test."));

        StartupConfigurationResult missing =
            new StartupConfigurationCoordinator(missingStore).Resolve();
        StartupConfigurationResult invalid =
            new StartupConfigurationCoordinator(invalidStore).Resolve();
        StartupConfigurationResult unavailable =
            new StartupConfigurationCoordinator(unavailableStore).Resolve();

        Assert.NotNull(missing.RuntimeSettings);
        Assert.Same(StarterConfiguration.Default, missing.RuntimeSettings);
        Assert.Null(invalid.RuntimeSettings);
        Assert.Null(unavailable.RuntimeSettings);
        AssertStoreCalls(missingStore);
        AssertStoreCalls(invalidStore);
        AssertStoreCalls(unavailableStore);
    }

    private static ApplicationSettings CreateLoadedSettings()
    {
        return new ApplicationSettings(
            new[]
            {
                new MacroDefinition(
                    SingleMacroCompatibility.LegacyMacroId,
                    "Loaded A",
                    true,
                    new KeyboardHotkey(
                        HotkeyKey.F9,
                        HotkeyModifiers.Control),
                    new MacroSequence(
                        new MacroStep[]
                        {
                            new KeyTapStep(KeyboardKey.M),
                            new DelayStep(TimeSpan.FromMilliseconds(250)),
                            new KeyTapStep(KeyboardKey.Enter)
                        }))
            });
    }

    private static void AssertStarterSemantics(ApplicationSettings settings)
    {
        MacroDefinition macro = Assert.Single(settings.Macros);
        Assert.Equal("ABC Test", macro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F8),
            macro.Hotkey);
        Assert.Collection(
            macro.Sequence.Steps,
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

    private static void AssertRecoveryStarter(
        StartupConfigurationResult result)
    {
        Assert.Same(StarterConfiguration.Default, result.InitialEditorSettings);
        Assert.Null(result.RuntimeSettings);

        ApplicationSettings starter = result.InitialEditorSettings;
        MacroDefinition macro = Assert.Single(starter.Macros);
        ConfigurationValidationResult validation =
            ConfigurationValidator.Validate(
                new ApplicationSettingsCandidate(
                    new MacroDefinitionCandidate?[]
                    {
                        new(
                            macro.Id,
                            macro.Name,
                            macro.Enabled,
                            macro.Hotkey,
                            macro.Sequence)
                    }));

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Settings);
        Assert.Empty(validation.Errors);
    }

    private static void AssertStoreCalls(RecordingSettingsStore store)
    {
        Assert.Equal(1, store.LoadCallCount);
        Assert.Equal(0, store.SaveCallCount);
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

        internal SettingsLoadResult LoadResult { get; set; } =
            SettingsLoadResult.Missing();

        internal int LoadCallCount { get; private set; }

        internal int SaveCallCount { get; private set; }

        public SettingsLoadResult Load()
        {
            LoadCallCount++;
            return LoadResult;
        }

        public SettingsSaveResult Save(ApplicationSettings settings)
        {
            SaveCallCount++;
            return SettingsSaveResult.Success();
        }
    }
}
