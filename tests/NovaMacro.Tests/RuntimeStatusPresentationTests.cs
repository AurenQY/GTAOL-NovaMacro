using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class RuntimeStatusPresentationTests
{
    [Fact]
    public void RegistrationFailure_UsesStartupSnapshotNameAndHotkey()
    {
        MacroDefinition startupMacro = Macro("Old Name", HotkeyKey.F8);
        var startup = new ApplicationSettings(new[] { startupMacro });
        MultiMacroEditorDraft editor = MultiMacroEditorDraft.From(startup);
        editor.SelectedMacro!.Name = "New Name";
        editor.SelectedMacro.HotkeyKey = HotkeyKey.F9;
        var status = new MultiMacroRuntimeStatus(
            MultiMacroRuntimeState.NoRegistrationsActive,
            new[]
            {
                new RuntimeMacroStatus(
                    startupMacro.Id,
                    RuntimeMacroState.RegistrationFailed,
                    "The key is already registered.")
            });

        RuntimeStatusPresentation presentation = RuntimeStatusPresentation.FromUpdate(
            status,
            startup);

        Assert.Equal(
            "Runtime inactive: no enabled macro registrations are active.",
            presentation.Message);
        Assert.Equal(
            new[] { "Old Name (F8): The key is already registered." },
            presentation.Diagnostics);
    }

    [Fact]
    public void ExecutionFailure_UsesStartupSnapshotWithoutChangingRuntimeState()
    {
        MacroDefinition startupMacro = Macro("Old Name", HotkeyKey.F8);
        var startup = new ApplicationSettings(new[] { startupMacro });

        string message = RuntimeStatusPresentation.FormatExecutionFailure(
            startupMacro.Id,
            new InvalidOperationException("Output failed."),
            startup);

        Assert.Equal("Old Name (F8) failed: Output failed.", message);
    }

    [Fact]
    public void InitialServiceUnavailable_UsesStartupActivationWording()
    {
        var startup = new ApplicationSettings(new[] { Macro("A", HotkeyKey.F8) });
        var status = new MultiMacroRuntimeStatus(
            MultiMacroRuntimeState.ServiceUnavailable,
            Array.Empty<RuntimeMacroStatus>());

        RuntimeStatusPresentation presentation =
            RuntimeStatusPresentation.FromInitial(status, startup);

        Assert.Contains("could not become available during startup", presentation.Message);
        Assert.DoesNotContain("became unavailable", presentation.Message);
    }

    [Fact]
    public void UpdateServiceUnavailable_UsesPostStartupWordingWithoutStartupFailureText()
    {
        var startup = new ApplicationSettings(new[] { Macro("A", HotkeyKey.F8) });
        var status = new MultiMacroRuntimeStatus(
            MultiMacroRuntimeState.ServiceUnavailable,
            Array.Empty<RuntimeMacroStatus>());

        RuntimeStatusPresentation presentation =
            RuntimeStatusPresentation.FromUpdate(status, startup);

        Assert.Contains("became unavailable after runtime startup", presentation.Message);
        Assert.DoesNotContain("could not start", presentation.Message);
        Assert.DoesNotContain("could not become available during startup", presentation.Message);
    }

    [Theory]
    [InlineData((int)MultiMacroRuntimeState.Active,
        "Runtime active: all enabled macros are registered.")]
    [InlineData((int)MultiMacroRuntimeState.PartiallyActive,
        "Runtime partially active: some enabled macros could not be registered.")]
    [InlineData((int)MultiMacroRuntimeState.NoEnabledMacros,
        "Runtime inactive: no macros are enabled.")]
    [InlineData((int)MultiMacroRuntimeState.NoRegistrationsActive,
        "Runtime inactive: no enabled macro registrations are active.")]
    public void NonServiceUnavailableAggregates_HaveTheSameInitialAndUpdateText(
        int stateValue,
        string expectedMessage)
    {
        MultiMacroRuntimeState state = (MultiMacroRuntimeState)stateValue;
        var startup = new ApplicationSettings(new[] { Macro("A", HotkeyKey.F8) });
        var status = new MultiMacroRuntimeStatus(
            state,
            Array.Empty<RuntimeMacroStatus>());

        Assert.Equal(
            expectedMessage,
            RuntimeStatusPresentation.FromInitial(status, startup).Message);
        Assert.Equal(
            expectedMessage,
            RuntimeStatusPresentation.FromUpdate(status, startup).Message);
    }

    private static MacroDefinition Macro(string name, HotkeyKey hotkey)
    {
        return new MacroDefinition(
            new MacroId(new Guid("11111111-1111-1111-1111-111111111111")),
            name,
            true,
            new KeyboardHotkey(hotkey),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }));
    }
}
