using NovaMacro.App;
using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.App.Startup;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class HeldMouseEditorPresentationTests
{
    [Fact]
    public void DraftRoundTripPreservesMouseTriggerModeAndLeftTap()
    {
        MacroDefinition original = new(
            Id(1),
            "Mouse repeat",
            true,
            new MouseButtonMacroTrigger(MouseButton.XButton2),
            MacroExecutionMode.RepeatWhileHeld,
            new MacroSequence(new MacroStep[]
            {
                new MouseButtonTapStep(MouseButton.Left),
                new KeyTapStep(KeyboardKey.NumpadEnter)
            }));
        MacroEditorDraft draft = MacroEditorDraft.From(original);

        Assert.Equal(MacroTriggerEditorKind.MouseButton, draft.TriggerKind);
        Assert.Equal(MouseButton.XButton2, draft.MouseButton);
        Assert.Equal(MacroExecutionMode.RepeatWhileHeld, draft.ExecutionMode);
        Assert.Equal("Mouse5", draft.TriggerDisplay);
        Assert.True(draft.IsMouseTrigger);
        Assert.Collection(
            draft.Steps,
            step => Assert.True(step.IsMouseButtonTap),
            step => Assert.Equal(KeyboardKey.NumpadEnter, step.Key));

        MacroDefinition rebuilt = Build(draft);
        Assert.Equal(original.Id, rebuilt.Id);
        Assert.Equal(original.Trigger, rebuilt.Trigger);
        Assert.Equal(original.ExecutionMode, rebuilt.ExecutionMode);
        Assert.Equal(original.Sequence.Steps, rebuilt.Sequence.Steps);
    }

    [Fact]
    public void TriggerSwitchingPreservesCoherentKeyboardAndMouseSubValues()
    {
        MacroDefinition original = new(
            Id(1),
            "Keyboard",
            true,
            new KeyboardMacroTrigger(new KeyboardHotkey(
                HotkeyKey.NumpadDivide,
                HotkeyModifiers.Control | HotkeyModifiers.Alt)),
            MacroExecutionMode.Once,
            KeySequence(KeyboardKey.Numpad0));
        MacroEditorDraft draft = MacroEditorDraft.From(original);

        Assert.Equal("Ctrl+Alt+NumpadDivide", draft.TriggerDisplay);
        draft.TriggerKind = MacroTriggerEditorKind.MouseButton;
        draft.MouseButton = MouseButton.XButton1;
        draft.ExecutionMode = MacroExecutionMode.RepeatWhileHeld;

        MacroDefinition mouse = Build(draft);
        Assert.Equal(
            new MouseButtonMacroTrigger(MouseButton.XButton1),
            mouse.Trigger);
        Assert.Equal(MacroExecutionMode.RepeatWhileHeld, mouse.ExecutionMode);

        draft.TriggerKind = MacroTriggerEditorKind.Keyboard;
        draft.ExecutionMode = MacroExecutionMode.Once;
        MacroDefinition keyboard = Build(draft);

        Assert.Equal(original.Trigger, keyboard.Trigger);
        Assert.Equal("Ctrl+Alt+NumpadDivide", draft.TriggerDisplay);
    }

    [Fact]
    public void KeyboardRepeatSelectionReturnsValidationInsteadOfSilentCoercion()
    {
        var editor = MultiMacroEditorDraft.From(new ApplicationSettings(new[]
        {
            new MacroDefinition(
                Id(1),
                "Keyboard",
                true,
                new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F8)),
                MacroExecutionMode.Once,
                KeySequence(KeyboardKey.A))
        }));
        editor.SelectedMacro!.ExecutionMode = MacroExecutionMode.RepeatWhileHeld;

        Assert.False(editor.TryBuildSettings(out _, out MacroEditorValidationResult result));
        Assert.Contains(result.ConfigurationErrors, error =>
            error.Code == ConfigurationValidationErrorCode.KeyboardRepeatWhileHeldNotAllowed &&
            error.Path == "macros[0].executionMode");
    }

    [Fact]
    public void AddMouseTapCreatesOnlySupportedLeftMouseStep()
    {
        var editor = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));
        MacroEditorDraft draft = editor.AddMacro();
        draft.TriggerKind = MacroTriggerEditorKind.MouseButton;
        draft.MouseButton = MouseButton.XButton1;
        draft.AddMouseButtonTap();

        Assert.True(editor.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.True(validation.IsValid);
        Assert.Contains(
            Assert.Single(settings!.Macros).Sequence.Steps,
            step => step == new MouseButtonTapStep(MouseButton.Left));
    }

    [Theory]
    [InlineData(MouseButton.XButton1, "Mouse4")]
    [InlineData(MouseButton.XButton2, "Mouse5")]
    public void StatusPresentationUsesCanonicalMouseTriggerDescription(
        MouseButton button,
        string expected)
    {
        MacroDefinition macro = new(
            Id(1),
            "Mouse",
            true,
            new MouseButtonMacroTrigger(button),
            MacroExecutionMode.Once,
            KeySequence(KeyboardKey.A));
        var settings = new ApplicationSettings(new[] { macro });
        var status = new MultiMacroRuntimeStatus(
            MultiMacroRuntimeState.NoRegistrationsActive,
            new[]
            {
                new RuntimeMacroStatus(
                    macro.Id,
                    RuntimeMacroState.RegistrationFailed,
                    "Mouse backend unavailable.")
            });

        RuntimeStatusPresentation presentation =
            RuntimeStatusPresentation.FromInitial(status, settings);

        Assert.Equal(
            new[] { $"Mouse ({expected}): Mouse backend unavailable." },
            presentation.Diagnostics);
        Assert.Equal(expected, RuntimeStatusPresentation.FormatTrigger(macro.Trigger));
    }

    private static MacroDefinition Build(MacroEditorDraft draft)
    {
        var editor = MultiMacroEditorDraft.From(
            new ApplicationSettings(new[]
            {
                new MacroDefinition(
                    draft.Id,
                    draft.Name,
                    draft.Enabled,
                    new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F8)),
                    MacroExecutionMode.Once,
                    KeySequence(KeyboardKey.A))
            }));
        MacroEditorDraft target = editor.SelectedMacro!;
        target.Name = draft.Name;
        target.Enabled = draft.Enabled;
        target.TriggerKind = draft.TriggerKind;
        target.HotkeyKey = draft.HotkeyKey;
        target.Control = draft.Control;
        target.Alt = draft.Alt;
        target.Shift = draft.Shift;
        target.MouseButton = draft.MouseButton;
        target.ExecutionMode = draft.ExecutionMode;
        while (target.Steps.Count > 0) target.Remove(target.Steps[0]);
        foreach (MacroStepEditorDraft step in draft.Steps)
        {
            if (step.IsKeyTap)
            {
                target.AddKeyTap();
                target.Steps[^1].Key = step.Key;
            }
            else if (step.IsMouseButtonTap) target.AddMouseButtonTap();
            else
            {
                target.AddDelay();
                target.Steps[^1].MillisecondsText = step.MillisecondsText;
            }
        }

        Assert.True(editor.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.True(validation.IsValid);
        return Assert.Single(settings!.Macros);
    }

    private static MacroSequence KeySequence(KeyboardKey key) =>
        new(new MacroStep[] { new KeyTapStep(key) });

    private static MacroId Id(int value) =>
        new(new Guid(value, 0, 0, new byte[8]));
}
