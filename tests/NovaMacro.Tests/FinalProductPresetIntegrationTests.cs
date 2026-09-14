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
    public void MissingSettings_ProvidesGtaFirstRunDefaultsRegistersTheirTriggersAndDoesNotWrite()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        var store = new JsonSettingsStore(path);

        StartupConfigurationResult startup =
            new StartupConfigurationCoordinator(store).Resolve();
        using var keyboardNative = new FakeGlobalHotkeyNativeApi();
        var keyboardScheduler = new FakeHotkeyTriggerWorkScheduler();
        var keyboardService = new WindowsGlobalHotkeyService(
            keyboardNative,
            keyboardScheduler);
        using var mouseNative = new FakeGlobalMouseButtonNativeApi();
        var mouseScheduler = new FakeHotkeyTriggerWorkScheduler();
        using var mouseService = new WindowsGlobalMouseButtonService(
            mouseNative,
            mouseScheduler);
        using var runtime = new MultiMacroHotkeyCoordinator(
            startup.RuntimeSettings!,
            () => keyboardService,
            new MacroRunner(
                new NoOpKeyboardOutput(),
                new NoOpMouseOutput(),
                new ImmediateDelay()),
            mouseButtonServiceFactory: () => mouseService);

        MultiMacroRuntimeStatus status = runtime.Start();

        Assert.Equal(StartupConfigurationStatus.StarterReady, startup.Status);
        Assert.Equal(BuiltInPresetCatalog.All.Select(preset => preset.Name),
            startup.InitialEditorSettings.Macros.Select(macro => macro.Name));
        Assert.Equal(BuiltInPresetCatalog.All.Select(preset => preset.Trigger),
            startup.RuntimeSettings!.Macros.Select(macro => macro.Trigger));
        Assert.Equal(BuiltInPresetCatalog.All.Select(preset => preset.ExecutionMode),
            startup.RuntimeSettings!.Macros.Select(macro => macro.ExecutionMode));
        Assert.All(startup.RuntimeSettings!.Macros, macro => Assert.True(macro.Enabled));
        Assert.All(startup.RuntimeSettings!.Macros, macro =>
            Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, macro.Id));
        Assert.Equal(4, startup.RuntimeSettings!.Macros.Select(macro => macro.Id)
            .Distinct().Count());
        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(new[] { 0x52u, 0x45u, 0x48u },
            keyboardNative.RegisterCalls.Select(call => call.VirtualKey));
        Assert.Equal(new[] { 0x4001u, 0x4001u, 0x4003u },
            keyboardNative.RegisterCalls.Select(call => call.Modifiers));
        Assert.DoesNotContain(keyboardNative.RegisterCalls,
            call => call.VirtualKey == 0x77u);
        Assert.Equal(1, mouseNative.InstallCallCount);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingSettings_AreLoadedWithoutGtaPresetInjectionOrAutoWrite(int version)
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string serialized = version switch
        {
            1 => SettingsJsonTestSupport.ValidJson,
            2 => SettingsJsonCodec.WriteV2(ExistingSettings()),
            3 => SettingsJsonCodec.WriteV3(ExistingSettings()),
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        byte[] original = Encoding.UTF8.GetBytes(serialized);
        File.WriteAllBytes(path, original);

        StartupConfigurationResult startup = new StartupConfigurationCoordinator(
            new JsonSettingsStore(path)).Resolve();

        Assert.Equal(StartupConfigurationStatus.SavedReady, startup.Status);
        Assert.Single(startup.InitialEditorSettings.Macros);
        Assert.Single(startup.RuntimeSettings!.Macros);
        Assert.DoesNotContain(startup.InitialEditorSettings.Macros,
            macro => BuiltInPresetCatalog.All.Any(preset => preset.Name == macro.Name));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ValidEmptySettings_RemainEmptyWithoutGtaPresetInjection(int version)
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var empty = new ApplicationSettings(Array.Empty<MacroDefinition>());
        File.WriteAllText(path, version == 2
            ? SettingsJsonCodec.WriteV2(empty)
            : SettingsJsonCodec.WriteV3(empty));
        byte[] original = File.ReadAllBytes(path);

        StartupConfigurationResult startup = new StartupConfigurationCoordinator(
            new JsonSettingsStore(path)).Resolve();

        Assert.Equal(StartupConfigurationStatus.SavedReady, startup.Status);
        Assert.Empty(startup.InitialEditorSettings.Macros);
        Assert.Empty(startup.RuntimeSettings!.Macros);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void InvalidSettings_RemainRecoveryOnlyWithoutGtaDefaultsOrAutoWrite()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] invalid = Encoding.UTF8.GetBytes("{\"version\":3,\"macros\":[}");
        File.WriteAllBytes(path, invalid);

        StartupConfigurationResult startup = new StartupConfigurationCoordinator(
            new JsonSettingsStore(path)).Resolve();

        Assert.Equal(StartupConfigurationStatus.InvalidRecovery, startup.Status);
        Assert.Empty(startup.InitialEditorSettings.Macros);
        Assert.Null(startup.RuntimeSettings);
        Assert.Equal(invalid, File.ReadAllBytes(path));
    }

    [Fact]
    public void SavedDeletionOfAFactoryMacro_RemainsAuthoritativeOnRestart()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        var store = new JsonSettingsStore(path);
        StartupConfigurationResult firstRun = new StartupConfigurationCoordinator(
            store).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            firstRun.InitialEditorSettings);

        Assert.True(draft.DeleteSelectedMacro());
        Assert.Equal(MacroEditorSaveStatus.Saved,
            new MacroEditorSaveCoordinator(store).Save(draft).Status);

        StartupConfigurationResult restarted = new StartupConfigurationCoordinator(
            store).Resolve();

        Assert.Equal(StartupConfigurationStatus.SavedReady, restarted.Status);
        Assert.Equal(3, restarted.InitialEditorSettings.Macros.Count);
        Assert.DoesNotContain(restarted.InitialEditorSettings.Macros,
            macro => macro.Name == "Buy All Ammo");
        Assert.DoesNotContain(restarted.InitialEditorSettings.Macros,
            macro => macro.Name == "New Macro");
    }

    private static ApplicationSettings ExistingSettings() => new(new[]
    {
        new MacroDefinition(
            new MacroId(new Guid("11111111-1111-1111-1111-111111111111")),
            "Existing macro",
            enabled: true,
            new KeyboardHotkey(HotkeyKey.F9),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.Z) }))
    });

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void KeyDown(KeyboardKey key) { }
        public void KeyUp(KeyboardKey key) { }
    }

    private sealed class NoOpMouseOutput : IMouseOutput
    {
        public void ButtonDown(MouseButton button) { }
        public void ButtonUp(MouseButton button) { }
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
