using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MainWindowMultiIntegrationTests
{
    [Fact]
    public void ProductionSaveBoundary_PersistsDraftWithoutReplacingStartupRuntime()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        ApplicationSettings startupSettings = Settings("Old Name", HotkeyKey.F8);
        WriteSettings(path, startupSettings);

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            StartupConfigurationResult startup = Resolve(store);
            var window = new MainWindow(new MacroEditorSaveCoordinator(store), startup);
            using var native = new FakeGlobalHotkeyNativeApi();
            var scheduler = new FakeHotkeyTriggerWorkScheduler();
            var service = new WindowsGlobalHotkeyService(native, scheduler);
            var runner = new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay());
            int factoryCalls = 0;
            using var runtime = new MultiMacroHotkeyCoordinator(
                startup.RuntimeSettings!,
                () =>
                {
                    factoryCalls++;
                    return service;
                },
                runner);

            window.PresentInitialRuntimeStatus(runtime.Start(), startup.RuntimeSettings!);
            window.Draft.SelectedMacro!.Name = "New Name";
            window.Draft.SelectedMacro.HotkeyKey = HotkeyKey.F9;
            Click(window, "SaveButton");

            ApplicationSettings saved = AssertLoaded(store.Load());
            MacroDefinition savedMacro = Assert.Single(saved.Macros);
            Assert.Equal("New Name", savedMacro.Name);
            Assert.Equal(HotkeyKey.F9, savedMacro.GetKeyboardHotkey().Key);
            Assert.Equal(new[] { 0x77u },
                native.RegisterCalls.Select(call => call.VirtualKey));
            Assert.Empty(native.UnregisterCalls);
            Assert.Equal(1, factoryCalls);
            Assert.Equal("Old Name", Assert.Single(startup.RuntimeSettings!.Macros).Name);
            Assert.Equal(HotkeyKey.F8,
                Assert.Single(startup.RuntimeSettings.Macros).GetKeyboardHotkey().Key);

            window.PresentExecutionFailure(
                Assert.Single(startup.RuntimeSettings.Macros).Id,
                new InvalidOperationException("Injected failure."),
                startup.RuntimeSettings);
            ItemsControl diagnostics = Named<ItemsControl>(
                window,
                "RuntimeDiagnosticsList");
            Assert.Equal(
                new[] { "Old Name (F8) failed: Injected failure." },
                diagnostics.Items.Cast<string>());
            TextBlock saveStatus = Named<TextBlock>(window, "SaveStatusText");
            Assert.Contains("after restarting NovaMacro", saveStatus.Text);
        });
    }

    [Fact]
    public void InvalidEditProductionSaveBoundary_LeavesCanonicalBytesUntouched()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        ApplicationSettings original = Settings("Valid", HotkeyKey.F8);
        WriteSettings(path, original);
        byte[] before = File.ReadAllBytes(path);

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            var window = new MainWindow(
                new MacroEditorSaveCoordinator(store),
                Resolve(store));
            window.Draft.SelectedMacro!.Name = "   ";

            Button save = Named<Button>(window, "SaveButton");
            Assert.False(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(before, File.ReadAllBytes(path));
            MultiMacroEditorControl editor = Assert.IsType<MultiMacroEditorControl>(
                Named<ContentControl>(window, "MultiEditorHost").Content);
            ItemsControl errors = Named<ItemsControl>(editor, "ValidationErrorsList");
            Assert.Contains("macros[0].name: Macro name is required.",
                errors.Items.Cast<string>());
        });
    }

    [Fact]
    public void InvalidRecovery_LeavesSourceBytesAndPresentsUsableStarterEditor()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] invalid = Encoding.UTF8.GetBytes("{\"version\":2,\"macros\":[}");
        File.WriteAllBytes(path, invalid);

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            StartupConfigurationResult startup = Resolve(store);
            var window = new MainWindow(new MacroEditorSaveCoordinator(store), startup);

            Assert.Equal(StartupConfigurationStatus.InvalidRecovery, startup.Status);
            Assert.Null(startup.RuntimeSettings);
            Assert.Equal(invalid, File.ReadAllBytes(path));
            Assert.Equal(SingleMacroCompatibility.LegacyMacroId,
                Assert.Single(window.Draft.Macros).Id);
            TextBlock runtimeStatus = Named<TextBlock>(window, "RuntimeStatusText");
            Assert.Contains("could not be loaded", runtimeStatus.Text);
            Assert.True(Named<ScrollViewer>(window,
                "RuntimeDiagnosticsScrollViewer").Visibility == Visibility.Visible);
        });
    }

    [Fact]
    public void EmptyV2ProductionPath_HasNoRuntimeRegistrationAndCanAddFirstMacro()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        WriteSettings(path, new ApplicationSettings(Array.Empty<MacroDefinition>()));

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            StartupConfigurationResult startup = Resolve(store);
            var window = new MainWindow(new MacroEditorSaveCoordinator(store), startup);
            var runner = new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay());
            int factoryCalls = 0;
            using var runtime = new MultiMacroHotkeyCoordinator(
                startup.RuntimeSettings!,
                () =>
                {
                    factoryCalls++;
                    throw new InvalidOperationException("Factory must not run.");
                },
                runner);

            MultiMacroRuntimeStatus status = runtime.Start();
            window.PresentInitialRuntimeStatus(status, startup.RuntimeSettings!);
            MultiMacroEditorControl editor = Assert.IsType<MultiMacroEditorControl>(
                Named<ContentControl>(window, "MultiEditorHost").Content);

            Assert.Equal(StartupConfigurationStatus.SavedReady, startup.Status);
            Assert.Empty(window.Draft.Macros);
            Assert.Null(window.Draft.SelectedMacro);
            Assert.Equal(MultiMacroRuntimeState.NoEnabledMacros, status.State);
            Assert.Equal(0, factoryCalls);

            window.Draft.AddMacro();

            Assert.Single(window.Draft.Macros);
            Assert.Same(window.Draft.Macros[0], window.Draft.SelectedMacro);
            Assert.Same(window.Draft, editor.DataContext);
        });
    }

    [Fact]
    public void MainWindow_OwnsOneMultiEditorBoundToItsOnlyDraft()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        WriteSettings(path, Settings("A", HotkeyKey.F8));

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            var window = new MainWindow(
                new MacroEditorSaveCoordinator(store),
                Resolve(store));
            ContentControl host = Named<ContentControl>(window, "MultiEditorHost");
            MultiMacroEditorControl editor = Assert.IsType<MultiMacroEditorControl>(
                host.Content);

            Assert.Same(window.Draft, editor.DataContext);
            Assert.IsNotType<MacroEditorDraft>(editor.DataContext);
        });
    }

    [Fact]
    public void PartialRegistrationFailure_PresentsFailedStartupMacroWithoutMutatingEditor()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        ApplicationSettings settings = Settings(
            ("11111111-1111-1111-1111-111111111111", "A", HotkeyKey.F8),
            ("22222222-2222-2222-2222-222222222222", "B", HotkeyKey.F9),
            ("33333333-3333-3333-3333-333333333333", "C", HotkeyKey.F10));
        WriteSettings(path, settings);

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            StartupConfigurationResult startup = Resolve(store);
            var window = new MainWindow(new MacroEditorSaveCoordinator(store), startup);
            using var native = new FakeGlobalHotkeyNativeApi();
            native.FailRegisterCall(2, nativeErrorCode: 1409);
            var scheduler = new FakeHotkeyTriggerWorkScheduler();
            var service = new WindowsGlobalHotkeyService(native, scheduler);
            using var runtime = new MultiMacroHotkeyCoordinator(
                startup.RuntimeSettings!,
                () => service,
                new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));

            MultiMacroRuntimeStatus status = runtime.Start();
            window.PresentInitialRuntimeStatus(status, startup.RuntimeSettings!);

            Assert.Equal(MultiMacroRuntimeState.PartiallyActive, status.State);
            Assert.Equal(
                new[]
                {
                    RuntimeMacroState.Active,
                    RuntimeMacroState.RegistrationFailed,
                    RuntimeMacroState.Active
                },
                status.Macros.Select(macro => macro.State));
            ItemsControl diagnostics = Named<ItemsControl>(
                window,
                "RuntimeDiagnosticsList");
            Assert.StartsWith("B (F9):", Assert.Single(diagnostics.Items.Cast<string>()));
            Assert.True(window.Draft.Macros.All(macro => macro.Enabled));
            Assert.Equal(new[] { "A", "B", "C" },
                window.Draft.Macros.Select(macro => macro.Name));
            Assert.Equal(new[] { 0x77u, 0x78u, 0x79u },
                native.RegisterCalls.Select(call => call.VirtualKey));
        });
    }

    [Fact]
    public void AllOrdinaryRegistrationFailures_KeepEditorUsableAndDoNotClaimServiceUnavailable()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("data", "settings.json");
        ApplicationSettings settings = Settings(
            ("11111111-1111-1111-1111-111111111111", "A", HotkeyKey.F8),
            ("22222222-2222-2222-2222-222222222222", "B", HotkeyKey.F9));
        WriteSettings(path, settings);

        RunInSta(() =>
        {
            var store = new JsonSettingsStore(path);
            StartupConfigurationResult startup = Resolve(store);
            var window = new MainWindow(new MacroEditorSaveCoordinator(store), startup);
            using var native = new FakeGlobalHotkeyNativeApi();
            native.FailRegisterCall(1, nativeErrorCode: 1409);
            native.FailRegisterCall(2, nativeErrorCode: 1409);
            var scheduler = new FakeHotkeyTriggerWorkScheduler();
            var service = new WindowsGlobalHotkeyService(native, scheduler);
            using var runtime = new MultiMacroHotkeyCoordinator(
                startup.RuntimeSettings!,
                () => service,
                new MacroRunner(new NoOpKeyboardOutput(), new ImmediateDelay()));

            MultiMacroRuntimeStatus status = runtime.Start();
            window.PresentInitialRuntimeStatus(status, startup.RuntimeSettings!);

            Assert.Equal(MultiMacroRuntimeState.NoRegistrationsActive, status.State);
            Assert.All(status.Macros,
                macro => Assert.Equal(RuntimeMacroState.RegistrationFailed, macro.State));
            Assert.Equal(2, Named<ItemsControl>(window,
                "RuntimeDiagnosticsList").Items.Count);
            Assert.Equal(new[] { "A", "B" },
                window.Draft.Macros.Select(macro => macro.Name));
            Assert.True(Named<Button>(window, "SaveButton").IsEnabled);
        });
    }

    private static StartupConfigurationResult Resolve(JsonSettingsStore store)
    {
        return new StartupConfigurationCoordinator(store).Resolve();
    }

    private static ApplicationSettings Settings(string name, HotkeyKey key)
    {
        return new ApplicationSettings(new[]
        {
            new MacroDefinition(
                new MacroId(new Guid("11111111-1111-1111-1111-111111111111")),
                name,
                true,
                new KeyboardHotkey(key),
                new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }))
        });
    }

    private static ApplicationSettings Settings(
        params (string Id, string Name, HotkeyKey Key)[] macros)
    {
        return new ApplicationSettings(macros.Select(macro => new MacroDefinition(
            new MacroId(new Guid(macro.Id)),
            macro.Name,
            true,
            new KeyboardHotkey(macro.Key),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }))));
    }

    private static void WriteSettings(string path, ApplicationSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SettingsJsonCodec.WriteV2(settings));
    }

    private static ApplicationSettings AssertLoaded(SettingsLoadResult result)
    {
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        return Assert.IsType<ApplicationSettings>(result.Settings);
    }

    private static void Click(FrameworkElement root, string name)
    {
        Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static T Named<T>(FrameworkElement root, string name)
        where T : FrameworkElement
    {
        return Assert.IsType<T>(root.FindName(name));
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(exception)
                .Throw();
        }
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
