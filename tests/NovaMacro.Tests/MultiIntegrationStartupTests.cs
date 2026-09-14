using System.Text;
using System.Text.Json;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MultiIntegrationStartupTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public void MissingSettings_UsesStarterForEditorAndRuntimeWithoutWriting()
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
        var runner = new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay());
        var factoryCalls = 0;
        using var runtime = new MultiMacroHotkeyCoordinator(
            startup.RuntimeSettings!,
            () =>
            {
                factoryCalls++;
                return service;
            },
            runner);

        MultiMacroRuntimeStatus status = runtime.Start();

        Assert.Equal(StartupConfigurationStatus.StarterReady, startup.Status);
        Assert.Equal(4, draft.Macros.Count);
        Assert.All(draft.Macros, macro => Assert.True(macro.Enabled));
        Assert.All(draft.Macros, macro =>
            Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, macro.Id));
        Assert.Equal(MultiMacroRuntimeState.Active, status.State);
        Assert.Equal(1, factoryCalls);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void V1Load_MigratesInMemoryWithoutWritingUntilExplicitMultiSave()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] original = Encoding.UTF8.GetBytes(SettingsJsonTestSupport.ValidJson);
        File.WriteAllBytes(path, original);
        var store = new JsonSettingsStore(path);

        StartupConfigurationResult startup =
            new StartupConfigurationCoordinator(store).Resolve();

        Assert.Equal(StartupConfigurationStatus.SavedReady, startup.Status);
        MacroDefinition migrated = Assert.Single(startup.RuntimeSettings!.Macros);
        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, migrated.Id);
        Assert.True(migrated.Enabled);
        Assert.Equal(original, File.ReadAllBytes(path));

        var save = new MacroEditorSaveCoordinator(store);
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            startup.InitialEditorSettings);
        Assert.Equal(MacroEditorSaveStatus.Saved, save.Save(draft).Status);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(3, document.RootElement.GetProperty("version").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("macros", out _));
    }

    [Fact]
    public void V2Load_PreservesAllMacrosForTheMultiEditorInOrder()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        ApplicationSettings expected = Settings(
            Macro("11111111-1111-1111-1111-111111111111", "A", true, HotkeyKey.F8),
            Macro("22222222-2222-2222-2222-222222222222", "B", false, HotkeyKey.F9),
            Macro("33333333-3333-3333-3333-333333333333", "C", true, HotkeyKey.F10));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SettingsJsonCodec.WriteV2(expected));

        StartupConfigurationResult startup = new StartupConfigurationCoordinator(
            new JsonSettingsStore(path)).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            startup.InitialEditorSettings);

        Assert.Equal(StartupConfigurationStatus.SavedReady, startup.Status);
        Assert.Equal(expected.Macros.Select(macro => macro.Id),
            draft.Macros.Select(macro => macro.Id));
        Assert.Equal(new[] { "A", "B", "C" },
            draft.Macros.Select(macro => macro.Name));
        Assert.Equal(new[] { true, false, true },
            draft.Macros.Select(macro => macro.Enabled));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EmptyV2OrV3Load_RemainsEmptyWithoutStarterInjection(int version)
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var empty = new ApplicationSettings(Array.Empty<MacroDefinition>());
        File.WriteAllText(
            path,
            version == 2
                ? SettingsJsonCodec.WriteV2(empty)
                : SettingsJsonCodec.WriteV3(empty));

        StartupConfigurationResult startup = new StartupConfigurationCoordinator(
            new JsonSettingsStore(path)).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            startup.InitialEditorSettings);

        Assert.Equal(StartupConfigurationStatus.SavedReady, startup.Status);
        Assert.Empty(startup.RuntimeSettings!.Macros);
        Assert.Empty(draft.Macros);
        Assert.Null(draft.SelectedMacro);
        Assert.True(draft.Validate().IsValid);
    }

    [Fact]
    public void SavingEditedDraft_DoesNotReconfigureTheStartupRuntime()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        ApplicationSettings startupSettings = Settings(
            Macro("11111111-1111-1111-1111-111111111111", "A", true, HotkeyKey.F8));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SettingsJsonCodec.WriteV2(startupSettings));
        var store = new JsonSettingsStore(path);
        StartupConfigurationResult startup = new StartupConfigurationCoordinator(store).Resolve();
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
            startup.InitialEditorSettings);
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        var runner = new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay());
        using var runtime = new MultiMacroHotkeyCoordinator(
            startup.RuntimeSettings!,
            () => service,
            runner);

        runtime.Start();
        draft.SelectedMacro!.HotkeyKey = HotkeyKey.F9;
        Assert.Equal(MacroEditorSaveStatus.Saved,
            new MacroEditorSaveCoordinator(store).Save(draft).Status);

        Assert.Equal(new[] { 0x77u },
            native.RegisterCalls.Select(call => call.VirtualKey));
        Assert.Equal(HotkeyKey.F8,
            Assert.Single(startup.RuntimeSettings!.Macros).GetKeyboardHotkey().Key);
    }

    [Fact]
    public void RuntimeStatusSequencing_NormalStartPresentsInitialAndSuppressesSynchronousUpdate()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        MultiMacroHotkeyCoordinator coordinator = CreateRuntime(service);
        var initials = new List<MultiMacroRuntimeStatus>();
        var updates = new List<MultiMacroRuntimeStatus>();

        try
        {
            MultiMacroRuntimeStatus initial =
                NovaMacro.App.App.StartAndPresentInitialRuntimeStatus(
                    coordinator,
                    initials.Add);

            NovaMacro.App.App.AttachRuntimeStatusUpdates(
                coordinator,
                initial,
                updates.Add);

            Assert.Equal(MultiMacroRuntimeState.Active, initial.State);
            Assert.Equal(new[] { initial }, initials);
            Assert.Empty(updates);
            Assert.Same(initial, coordinator.CurrentStatus);
            Assert.Equal(
                "Runtime: Active",
                RuntimeStatusPresentation.FromInitial(
                    initial,
                    RuntimeSettings()).Message);
        }
        finally
        {
            _ = CaptureException(coordinator.Dispose);
        }
    }

    [Fact]
    public async Task RuntimeStatusSequencing_LaterTerminalTransitionPresentsUpdate()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        MultiMacroHotkeyCoordinator coordinator = CreateRuntime(service);
        var initials = new List<MultiMacroRuntimeStatus>();
        var terminalUpdate = new TaskCompletionSource<MultiMacroRuntimeStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            MultiMacroRuntimeStatus initial =
                NovaMacro.App.App.StartAndPresentInitialRuntimeStatus(
                    coordinator,
                    initials.Add);
            NovaMacro.App.App.AttachRuntimeStatusUpdates(
                coordinator,
                initial,
                status => terminalUpdate.TrySetResult(status));

            native.FailNextGetMessage();
            MultiMacroRuntimeStatus update = await terminalUpdate.Task.WaitAsync(
                SafetyTimeout);

            RuntimeStatusPresentation presentation =
                RuntimeStatusPresentation.FromUpdate(update, RuntimeSettings());
            Assert.Equal(MultiMacroRuntimeState.Active, initial.State);
            Assert.Single(initials);
            Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, update.State);
            Assert.Contains(
                "became unavailable after runtime startup",
                presentation.Message);
            Assert.DoesNotContain("could not start", presentation.Message);
        }
        finally
        {
            _ = CaptureException(coordinator.Dispose);
        }
    }

    [Fact]
    public void RuntimeStatusSequencing_InitialServiceUnavailableRemainsInitial()
    {
        MultiMacroHotkeyCoordinator coordinator = new(
            RuntimeSettings(),
            () => throw new InvalidOperationException("Activation failure."),
            new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));
        var initials = new List<MultiMacroRuntimeStatus>();
        var updates = new List<MultiMacroRuntimeStatus>();

        try
        {
            MultiMacroRuntimeStatus initial =
                NovaMacro.App.App.StartAndPresentInitialRuntimeStatus(
                    coordinator,
                    initials.Add);
            NovaMacro.App.App.AttachRuntimeStatusUpdates(
                coordinator,
                initial,
                updates.Add);

            RuntimeStatusPresentation presentation =
                RuntimeStatusPresentation.FromInitial(initial, RuntimeSettings());
            Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, initial.State);
            Assert.Equal(new[] { initial }, initials);
            Assert.Empty(updates);
            Assert.Contains(
                "could not become available during startup",
                presentation.Message);
        }
        finally
        {
            _ = CaptureException(coordinator.Dispose);
        }
    }

    [Fact]
    public async Task RuntimeStatusSequencing_TerminalBeforeSubscriptionUsesCurrentStatusCatchUp()
    {
        using var native = new FakeGlobalHotkeyNativeApi();
        var scheduler = new FakeHotkeyTriggerWorkScheduler();
        var service = new WindowsGlobalHotkeyService(native, scheduler);
        MultiMacroHotkeyCoordinator coordinator = CreateRuntime(service);
        var initials = new List<MultiMacroRuntimeStatus>();
        var terminalObserved = new TaskCompletionSource<MultiMacroRuntimeStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<MultiMacroRuntimeStatus> terminalObserver = status =>
        {
            if (status.State == MultiMacroRuntimeState.ServiceUnavailable)
            {
                terminalObserved.TrySetResult(status);
            }
        };

        try
        {
            MultiMacroRuntimeStatus initial =
                NovaMacro.App.App.StartAndPresentInitialRuntimeStatus(
                    coordinator,
                    initials.Add);
            coordinator.StatusChanged += terminalObserver;

            native.FailNextGetMessage();
            MultiMacroRuntimeStatus terminal = await terminalObserved.Task.WaitAsync(
                SafetyTimeout);
            coordinator.StatusChanged -= terminalObserver;

            Assert.Equal(MultiMacroRuntimeState.Active, initial.State);
            Assert.Equal(MultiMacroRuntimeState.ServiceUnavailable, terminal.State);
            Assert.Equal(
                MultiMacroRuntimeState.ServiceUnavailable,
                coordinator.CurrentStatus?.State);

            var updates = new List<MultiMacroRuntimeStatus>();
            NovaMacro.App.App.AttachRuntimeStatusUpdates(
                coordinator,
                initial,
                updates.Add);

            MultiMacroRuntimeStatus update = Assert.Single(updates);
            RuntimeStatusPresentation presentation =
                RuntimeStatusPresentation.FromUpdate(update, RuntimeSettings());
            Assert.Same(terminal, update);
            Assert.Contains(
                "became unavailable after runtime startup",
                presentation.Message);
            Assert.DoesNotContain("could not start", presentation.Message);
        }
        finally
        {
            coordinator.StatusChanged -= terminalObserver;
            _ = CaptureException(coordinator.Dispose);
        }
    }

    [Fact]
    public void RuntimeStatusSequencing_EarlyPartialSnapshotAlsoRequiresCatchUp()
    {
        var initial = new MultiMacroRuntimeStatus(
            MultiMacroRuntimeState.Active,
            Array.Empty<RuntimeMacroStatus>());
        var partial = new MultiMacroRuntimeStatus(
            MultiMacroRuntimeState.PartiallyActive,
            Array.Empty<RuntimeMacroStatus>());

        Assert.False(NovaMacro.App.App.ShouldPresentCurrentStatusAsUpdate(
            initial,
            initial));
        Assert.True(NovaMacro.App.App.ShouldPresentCurrentStatusAsUpdate(
            initial,
            partial));
    }

    private static ApplicationSettings Settings(params MacroDefinition[] macros)
    {
        return new ApplicationSettings(macros);
    }

    private static ApplicationSettings RuntimeSettings()
    {
        return Settings(Macro(
            "11111111-1111-1111-1111-111111111111",
            "Runtime macro",
            true,
            HotkeyKey.F8));
    }

    private static MultiMacroHotkeyCoordinator CreateRuntime(
        WindowsGlobalHotkeyService service)
    {
        return new MultiMacroHotkeyCoordinator(
            RuntimeSettings(),
            () => service,
            new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static MacroDefinition Macro(
        string id,
        string name,
        bool enabled,
        HotkeyKey hotkey)
    {
        return new MacroDefinition(
            new MacroId(new Guid(id)),
            name,
            enabled,
            new KeyboardHotkey(hotkey),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }));
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
