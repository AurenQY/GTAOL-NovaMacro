using System.Text;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.App.Presets;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class FinalProductPresetIntegrationTests
{
    [Fact]
    public void MissingSettings_UsesOnlyLegacyStarterWhileCatalogRemainsAvailable()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        var store = new JsonSettingsStore(path);

        StartupConfigurationResult startup =
            new StartupConfigurationCoordinator(store).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            startup.InitialEditorSettings);
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        using var runtime = new MultiMacroHotkeyCoordinator(
            startup.RuntimeSettings!,
            () => service,
            new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));

        MultiMacroRuntimeStatus status = runtime.Start();
        MacroEditorDraft starter = Assert.Single(draft.Macros);
        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? draftSettings,
            out MacroEditorValidationResult validation));
        Assert.True(validation.IsValid);

        Assert.Equal(StartupConfigurationStatus.StarterReady, startup.Status);
        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, starter.Id);
        Assert.True(starter.Enabled);
        Assert.Equal(HotkeyKey.F8, starter.HotkeyKey);
        Assert.Equal(BuiltInMacroSequences.AbcTest.Steps,
            Assert.Single(draftSettings!.Macros).Sequence.Steps);
        Assert.Single(BuiltInPresetCatalog.All);
        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(new[] { 0x77u },
            native.RegisterCalls.Select(call => call.VirtualKey));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void V1Migration_AddPresetAndExplicitSaveWritesTwoOrdinaryV2Macros()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] v1 = Encoding.UTF8.GetBytes(SettingsJsonTestSupport.ValidJson);
        File.WriteAllBytes(path, v1);
        var store = new JsonSettingsStore(path);

        StartupConfigurationResult startup =
            new StartupConfigurationCoordinator(store).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            startup.InitialEditorSettings);
        MacroEditorDraft migrated = Assert.Single(draft.Macros);
        MacroEditorDraft presetCopy = draft.AddFromPreset(
            Assert.Single(BuiltInPresetCatalog.All));
        presetCopy.HotkeyKey = HotkeyKey.F9;

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, migrated.Id);
        Assert.True(migrated.Enabled);
        Assert.Equal(v1, File.ReadAllBytes(path));
        Assert.Equal(MacroEditorSaveStatus.Saved,
            new MacroEditorSaveCoordinator(store).Save(draft).Status);

        string v2 = File.ReadAllText(path);
        ApplicationSettings reloaded = AssertLoaded(store.Load());

        Assert.Equal(2, reloaded.Macros.Count);
        Assert.Equal(new[] { migrated.Id, presetCopy.Id },
            reloaded.Macros.Select(macro => macro.Id));
        Assert.Equal(new[] { migrated.Name, "ABC Test" },
            reloaded.Macros.Select(macro => macro.Name));
        Assert.Equal(new[] { HotkeyKey.F8, HotkeyKey.F9 },
            reloaded.Macros.Select(macro => macro.Hotkey.Key));
        Assert.Equal(BuiltInMacroSequences.AbcTest.Steps,
            reloaded.Macros[1].Sequence.Steps);
        Assert.DoesNotContain("preset", v2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template", v2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", v2, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedPresetCopy_DoesNotLiveApplyButRegistersWithAllMacrosAfterRestart()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        var store = new JsonSettingsStore(path);
        ApplicationSettings original = Settings(
            Macro("11111111-1111-1111-1111-111111111111", "A", HotkeyKey.F8),
            Macro("22222222-2222-2222-2222-222222222222", "B", HotkeyKey.F10));
        Assert.True(store.Save(original).IsSuccess);

        StartupConfigurationResult initial =
            new StartupConfigurationCoordinator(store).Resolve();

        using (var native = new FakeGlobalHotkeyNativeApi())
        {
            var scheduler = new FakeHotkeyTriggerWorkScheduler();
            var service = new WindowsGlobalHotkeyService(native, scheduler);
            using var runtime = new MultiMacroHotkeyCoordinator(
                initial.RuntimeSettings!,
                () => service,
                new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));

            Assert.Equal(MultiMacroRuntimeState.Active, runtime.Start().State);
            var draft = MultiMacroEditorDraft.From(initial.InitialEditorSettings);
            MacroEditorDraft copy = draft.AddFromPreset(
                Assert.Single(BuiltInPresetCatalog.All));
            copy.HotkeyKey = HotkeyKey.F9;

            Assert.Equal(MacroEditorSaveStatus.Saved,
                new MacroEditorSaveCoordinator(store).Save(draft).Status);
            Assert.Equal(new[] { 0x77u, 0x79u },
                native.RegisterCalls.Select(call => call.VirtualKey));
            Assert.Equal(2, initial.RuntimeSettings!.Macros.Count);
        }

        StartupConfigurationResult restarted =
            new StartupConfigurationCoordinator(store).Resolve();

        using var restartNative = new FakeGlobalHotkeyNativeApi();
        var restartScheduler = new FakeHotkeyTriggerWorkScheduler();
        var restartService = new WindowsGlobalHotkeyService(
            restartNative,
            restartScheduler);
        using var restartedRuntime = new MultiMacroHotkeyCoordinator(
            restarted.RuntimeSettings!,
            () => restartService,
            new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));

        Assert.Equal(MultiMacroRuntimeState.Active, restartedRuntime.Start().State);
        Assert.Equal(new[] { "A", "B", "ABC Test" },
            restarted.RuntimeSettings!.Macros.Select(macro => macro.Name));
        Assert.Equal(new[] { 0x77u, 0x79u, 0x78u },
            restartNative.RegisterCalls.Select(call => call.VirtualKey));
    }

    [Fact]
    public void InvalidRecovery_AddPresetThenExplicitSavePublishesCompleteV2()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] invalid = Encoding.UTF8.GetBytes("{\"version\":2,\"macros\":[}");
        File.WriteAllBytes(path, invalid);
        var store = new JsonSettingsStore(path);

        StartupConfigurationResult recovery =
            new StartupConfigurationCoordinator(store).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            recovery.InitialEditorSettings);
        MacroEditorDraft copy = draft.AddFromPreset(
            Assert.Single(BuiltInPresetCatalog.All));
        copy.HotkeyKey = HotkeyKey.F9;

        Assert.Equal(StartupConfigurationStatus.InvalidRecovery, recovery.Status);
        Assert.Null(recovery.RuntimeSettings);
        Assert.Equal(invalid, File.ReadAllBytes(path));
        Assert.Equal(MacroEditorSaveStatus.Saved,
            new MacroEditorSaveCoordinator(store).Save(draft).Status);

        ApplicationSettings saved = AssertLoaded(store.Load());
        Assert.Equal(2, saved.Macros.Count);
        Assert.Equal(new[] { HotkeyKey.F8, HotkeyKey.F9 },
            saved.Macros.Select(macro => macro.Hotkey.Key));
        Assert.Equal(BuiltInMacroSequences.AbcTest.Steps,
            saved.Macros[1].Sequence.Steps);
    }

    [Fact]
    public void TestLocalFutureTemplates_CopyInCallerOrderAndUseNormalHotkeyValidation()
    {
        var firstTemplate = new BuiltInPresetDefinition(
            "first-template",
            "Same display name",
            enabled: true,
            new KeyboardHotkey(HotkeyKey.F8),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }));
        var secondTemplate = new BuiltInPresetDefinition(
            "second-template",
            "Same display name",
            enabled: true,
            new KeyboardHotkey(HotkeyKey.F8),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.B) }));
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        MacroEditorDraft first = draft.AddFromPreset(firstTemplate);
        MacroEditorDraft second = draft.AddFromPreset(secondTemplate);
        MacroEditorValidationResult conflicting = draft.Validate();

        Assert.Equal(new[] { "Same display name", "Same display name" },
            draft.Macros.Select(macro => macro.Name));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(KeyboardKey.A, first.Steps.Single().Key);
        Assert.Equal(KeyboardKey.B, second.Steps.Single().Key);
        Assert.Contains(conflicting.ConfigurationErrors,
            error => error.Code == ConfigurationValidationErrorCode.DuplicateEnabledHotkey);

        second.Enabled = false;

        Assert.True(draft.Validate().IsValid);
    }

    private static ApplicationSettings Settings(params MacroDefinition[] macros)
    {
        return new ApplicationSettings(macros);
    }

    private static MacroDefinition Macro(string id, string name, HotkeyKey hotkey)
    {
        return new MacroDefinition(
            new MacroId(new Guid(id)),
            name,
            enabled: true,
            new KeyboardHotkey(hotkey),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.Z) }));
    }

    private static ApplicationSettings AssertLoaded(SettingsLoadResult result)
    {
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        return Assert.IsType<ApplicationSettings>(result.Settings);
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void KeyDown(KeyboardKey key)
        {
        }

        public void KeyUp(KeyboardKey key)
        {
        }
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
