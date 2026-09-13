using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class HeldMouseNumpadConfigurationTests
{
    [Theory]
    [InlineData(false, MouseButton.XButton1, MacroExecutionMode.Once)]
    [InlineData(false, MouseButton.XButton2, MacroExecutionMode.Once)]
    [InlineData(false, MouseButton.XButton1, MacroExecutionMode.RepeatWhileHeld)]
    [InlineData(false, MouseButton.XButton2, MacroExecutionMode.RepeatWhileHeld)]
    [InlineData(true, MouseButton.Left, MacroExecutionMode.Once)]
    public void TriggerAndExecutionMode_ApprovedCombinationsAreValid(
        bool keyboard,
        MouseButton button,
        MacroExecutionMode mode)
    {
        MacroTrigger trigger = keyboard
            ? new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.Numpad0))
            : new MouseButtonMacroTrigger(button);

        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            Candidate(Macro(1, true, trigger, mode, Key(KeyboardKey.NumpadEnter))));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void KeyboardRepeatWhileHeldIsRejectedAtIndexedExecutionModePath()
    {
        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            Candidate(Macro(
                1,
                true,
                new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F8)),
                MacroExecutionMode.RepeatWhileHeld,
                Key(KeyboardKey.A))));

        Assert.Contains(result.Errors, error =>
            error.Code == ConfigurationValidationErrorCode.KeyboardRepeatWhileHeldNotAllowed &&
            error.Path == "macros[0].executionMode");
    }

    [Theory]
    [InlineData(MouseButton.XButton1)]
    [InlineData(MouseButton.XButton2)]
    public void DuplicateEnabledMouseTriggerIsRejected(MouseButton button)
    {
        MacroTrigger trigger = new MouseButtonMacroTrigger(button);

        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            Candidate(
                Macro(1, true, trigger, MacroExecutionMode.Once, Key(KeyboardKey.A)),
                Macro(2, true, trigger, MacroExecutionMode.Once, Key(KeyboardKey.B))));

        ConfigurationValidationError error = Assert.Single(result.Errors);
        Assert.Equal(ConfigurationValidationErrorCode.DuplicateEnabledTrigger, error.Code);
        Assert.Equal("macros[1].trigger", error.Path);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void DuplicateTriggerIsAllowedUnlessBothMacrosAreEnabled(
        bool firstEnabled,
        bool secondEnabled)
    {
        MacroTrigger trigger = new MouseButtonMacroTrigger(MouseButton.XButton1);

        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            Candidate(
                Macro(1, firstEnabled, trigger, MacroExecutionMode.Once, Key(KeyboardKey.A)),
                Macro(2, secondEnabled, trigger, MacroExecutionMode.Once, Key(KeyboardKey.B))));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DifferentTriggerKindsDoNotConflict()
    {
        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            Candidate(
                Macro(
                    1,
                    true,
                    new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F8)),
                    MacroExecutionMode.Once,
                    Key(KeyboardKey.A)),
                Macro(
                    2,
                    true,
                    new MouseButtonMacroTrigger(MouseButton.XButton1),
                    MacroExecutionMode.Once,
                    Key(KeyboardKey.B))));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyRepeatHasSpecificErrorAndOncePreservesExistingShapeRule()
    {
        MacroSequence empty = new(Array.Empty<MacroStep>());
        MacroTrigger mouse = new MouseButtonMacroTrigger(MouseButton.XButton1);

        ConfigurationValidationResult repeat = ConfigurationValidator.Validate(
            Candidate(Macro(
                1, true, mouse, MacroExecutionMode.RepeatWhileHeld, empty)));
        ConfigurationValidationResult once = ConfigurationValidator.Validate(
            Candidate(Macro(2, true, mouse, MacroExecutionMode.Once, empty)));

        Assert.Contains(repeat.Errors, error =>
            error.Code == ConfigurationValidationErrorCode.EmptyRepeatingSequenceNotAllowed &&
            error.Path == "macros[0].steps");
        Assert.DoesNotContain(once.Errors, error =>
            error.Code == ConfigurationValidationErrorCode.EmptyRepeatingSequenceNotAllowed);
        Assert.Contains(once.Errors, error =>
            error.Code == ConfigurationValidationErrorCode.MacroStepCountOutOfRange);
    }

    [Fact]
    public void V3RoundTripPreservesTriggersModesStepsIdsAndOrder()
    {
        var original = new ApplicationSettings(new[]
        {
            Definition(
                1,
                "Keyboard",
                new KeyboardMacroTrigger(new KeyboardHotkey(
                    HotkeyKey.NumpadDivide,
                    HotkeyModifiers.Control | HotkeyModifiers.Shift)),
                MacroExecutionMode.Once,
                new MacroStep[]
                {
                    new KeyTapStep(KeyboardKey.Numpad0),
                    new KeyTapStep(KeyboardKey.NumpadEnter)
                }),
            Definition(
                2,
                "Mouse4 repeat",
                new MouseButtonMacroTrigger(MouseButton.XButton1),
                MacroExecutionMode.RepeatWhileHeld,
                new MacroStep[]
                {
                    new MouseButtonTapStep(MouseButton.Left),
                    new DelayStep(TimeSpan.FromMilliseconds(15)),
                    new KeyTapStep(KeyboardKey.Digit5)
                }),
            Definition(
                3,
                "Mouse5 once",
                new MouseButtonMacroTrigger(MouseButton.XButton2),
                MacroExecutionMode.Once,
                new MacroStep[] { new KeyTapStep(KeyboardKey.NumpadAdd) })
        });

        string json = SettingsJsonCodec.WriteV3(original);
        SettingsJsonReadResult read = SettingsJsonCodec.ReadV1OrV2OrV3(json);

        Assert.True(read.IsSuccess);
        ApplicationSettings restored = read.Settings!;
        Assert.Equal(original.Macros.Select(m => m.Id), restored.Macros.Select(m => m.Id));
        Assert.Equal(original.Macros.Select(m => m.Name), restored.Macros.Select(m => m.Name));
        Assert.Equal(original.Macros.Select(m => m.Trigger), restored.Macros.Select(m => m.Trigger));
        Assert.Equal(original.Macros.Select(m => m.ExecutionMode), restored.Macros.Select(m => m.ExecutionMode));
        Assert.Equal(
            original.Macros.SelectMany(m => m.Sequence.Steps),
            restored.Macros.SelectMany(m => m.Sequence.Steps));
        Assert.Equal(json, SettingsJsonCodec.WriteV3(restored));
    }

    [Theory]
    [InlineData(MouseButton.XButton1, MacroExecutionMode.Once)]
    [InlineData(MouseButton.XButton2, MacroExecutionMode.Once)]
    [InlineData(MouseButton.XButton1, MacroExecutionMode.RepeatWhileHeld)]
    [InlineData(MouseButton.XButton2, MacroExecutionMode.RepeatWhileHeld)]
    public void V3RoundTripPreservesEveryMouseTriggerAndModeCombination(
        MouseButton button,
        MacroExecutionMode mode)
    {
        var settings = new ApplicationSettings(new[]
        {
            Definition(
                1,
                "Mouse",
                new MouseButtonMacroTrigger(button),
                mode,
                new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) })
        });

        MacroDefinition restored = Assert.Single(
            SettingsJsonCodec.ReadV1OrV2OrV3(
                SettingsJsonCodec.WriteV3(settings)).Settings!.Macros);

        Assert.Equal(new MouseButtonMacroTrigger(button), restored.Trigger);
        Assert.Equal(mode, restored.ExecutionMode);
        Assert.Equal(
            new MouseButtonTapStep(MouseButton.Left),
            Assert.Single(restored.Sequence.Steps));
    }

    [Theory]
    [InlineData("top", 8, "extra")]
    [InlineData("macro", 8, "macros[0].extra")]
    [InlineData("trigger", 8, "macros[0].trigger.extra")]
    [InlineData("step", 8, "macros[0].steps[0].extra")]
    public void V3RejectsUnknownPropertiesAtExactPath(
        string location,
        int expectedCode,
        string expectedPath)
    {
        string json = ValidV3Json();
        json = location switch
        {
            "top" => json.Replace("\"macros\"", "\"extra\":1,\"macros\""),
            "macro" => json.Replace("\"id\"", "\"extra\":1,\"id\""),
            "trigger" => json.Replace("\"kind\"", "\"extra\":1,\"kind\""),
            "step" => json.Replace("\"type\"", "\"extra\":1,\"type\""),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };

        AssertCodecFailure(
            json,
            (SettingsJsonErrorCode)expectedCode,
            expectedPath);
    }

    [Theory]
    [InlineData("kind", 18, "macros[0].trigger.kind")]
    [InlineData("mode", 20, "macros[0].executionMode")]
    [InlineData("button", 19, "macros[0].trigger.button")]
    public void V3RejectsUnknownTriggerVocabulary(
        string field,
        int expectedCode,
        string expectedPath)
    {
        string json = ValidV3Json(mouse: true);
        json = field switch
        {
            "kind" => json.Replace("\"mouseButton\"", "\"unknown\""),
            "mode" => json.Replace("\"once\"", "\"unknown\""),
            "button" => json.Replace("\"xButton1\"", "\"unknown\""),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        AssertCodecFailure(
            json,
            (SettingsJsonErrorCode)expectedCode,
            expectedPath);
    }

    [Fact]
    public void V3RejectsDuplicatePropertiesStructurally()
    {
        string json = ValidV3Json().Replace(
            "\"version\": 3",
            "\"version\": 3, \"version\": 3");

        AssertCodecFailure(
            json,
            SettingsJsonErrorCode.DuplicateProperty,
            "version");
    }

    [Fact]
    public void V3RejectsUnknownMouseOutputButtonStructurally()
    {
        var settings = new ApplicationSettings(new[]
        {
            Definition(
                1,
                "Mouse output",
                new MouseButtonMacroTrigger(MouseButton.XButton1),
                MacroExecutionMode.Once,
                new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) })
        });
        string json = SettingsJsonCodec.WriteV3(settings)
            .Replace("\"left\"", "\"right\"");

        AssertCodecFailure(
            json,
            SettingsJsonErrorCode.UnknownOutputMouseButton,
            "macros[0].steps[0].button");
    }

    [Fact]
    public void V3KeyboardRepeatAndEmptyMouseRepeatFailCanonicalValidation()
    {
        string keyboardRepeat = ValidV3Json()
            .Replace("\"once\"", "\"repeatWhileHeld\"");
        JsonNode emptyNode = JsonNode.Parse(ValidV3Json(mouse: true))!;
        JsonNode emptyMacro = emptyNode["macros"]![0]!;
        emptyMacro["executionMode"] = "repeatWhileHeld";
        emptyMacro["steps"] = new JsonArray();
        string emptyRepeat = emptyNode.ToJsonString();

        SettingsJsonReadResult keyboard =
            SettingsJsonCodec.ReadV1OrV2OrV3(keyboardRepeat);
        SettingsJsonReadResult empty =
            SettingsJsonCodec.ReadV1OrV2OrV3(emptyRepeat);

        Assert.Empty(keyboard.CodecErrors);
        Assert.Contains(keyboard.ValidationErrors, error =>
            error.Code == ConfigurationValidationErrorCode.KeyboardRepeatWhileHeldNotAllowed);
        Assert.Empty(empty.CodecErrors);
        Assert.Contains(empty.ValidationErrors, error =>
            error.Code == ConfigurationValidationErrorCode.EmptyRepeatingSequenceNotAllowed);
    }

    [Fact]
    public void V1AndV2MigrateToKeyboardOnceWithoutAutoWriteAndExplicitSaveWritesV3()
    {
        string v1 = SettingsJsonTestSupport.ValidJson;
        var v2Settings = new ApplicationSettings(new[]
        {
            Definition(
                9,
                "V2",
                new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F9)),
                MacroExecutionMode.Once,
                new MacroStep[] { new KeyTapStep(KeyboardKey.B) })
        });
        string v2 = SettingsJsonCodec.WriteV2(v2Settings);

        foreach (string source in new[] { v1, v2 })
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, "settings.json");
            File.WriteAllText(path, source, new UTF8Encoding(false));
            byte[] originalBytes = File.ReadAllBytes(path);
            var store = new JsonSettingsStore(path);

            SettingsLoadResult loaded = store.Load();

            Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
            MacroDefinition macro = Assert.Single(loaded.Settings!.Macros);
            Assert.IsType<KeyboardMacroTrigger>(macro.Trigger);
            Assert.Equal(MacroExecutionMode.Once, macro.ExecutionMode);
            if (source == v1)
            {
                Assert.Equal(SingleMacroCompatibility.LegacyMacroId, macro.Id);
            }
            Assert.Equal(originalBytes, File.ReadAllBytes(path));

            Assert.True(store.Save(loaded.Settings).IsSuccess);
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(3, saved.RootElement.GetProperty("version").GetInt32());
        }
    }

    [Fact]
    public void V2MigrationPreservesEveryMacroIdOrderNameEnabledHotkeyAndStep()
    {
        var original = new ApplicationSettings(new[]
        {
            new MacroDefinition(
                Id(4),
                "First disabled",
                false,
                new KeyboardHotkey(HotkeyKey.F9, HotkeyModifiers.Alt),
                Key(KeyboardKey.NumpadDecimal)),
            new MacroDefinition(
                Id(5),
                "Second enabled",
                true,
                new KeyboardHotkey(HotkeyKey.NumpadAdd),
                Key(KeyboardKey.NumpadMultiply))
        });

        ApplicationSettings restored = SettingsJsonCodec.ReadV1OrV2OrV3(
            SettingsJsonCodec.WriteV2(original)).Settings!;

        Assert.Equal(original.Macros.Select(m => m.Id), restored.Macros.Select(m => m.Id));
        Assert.Equal(original.Macros.Select(m => m.Name), restored.Macros.Select(m => m.Name));
        Assert.Equal(original.Macros.Select(m => m.Enabled), restored.Macros.Select(m => m.Enabled));
        Assert.Equal(
            original.Macros.Select(m => ((KeyboardMacroTrigger)m.Trigger).Hotkey),
            restored.Macros.Select(m => ((KeyboardMacroTrigger)m.Trigger).Hotkey));
        Assert.All(restored.Macros, macro =>
            Assert.Equal(MacroExecutionMode.Once, macro.ExecutionMode));
        Assert.Equal(
            original.Macros.SelectMany(m => m.Sequence.Steps),
            restored.Macros.SelectMany(m => m.Sequence.Steps));
    }

    [Fact]
    public void V2AndV3EmptySettingsRemainEmpty()
    {
        string v2 = SettingsJsonCodec.WriteV2(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));
        string v3 = SettingsJsonCodec.WriteV3(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        Assert.Empty(SettingsJsonCodec.ReadV1OrV2OrV3(v2).Settings!.Macros);
        Assert.Empty(SettingsJsonCodec.ReadV1OrV2OrV3(v3).Settings!.Macros);
    }

    private static string ValidV3Json(bool mouse = false)
    {
        MacroTrigger trigger = mouse
            ? new MouseButtonMacroTrigger(MouseButton.XButton1)
            : new KeyboardMacroTrigger(new KeyboardHotkey(HotkeyKey.F8));
        return SettingsJsonCodec.WriteV3(new ApplicationSettings(new[]
        {
            Definition(
                1,
                "Example",
                trigger,
                MacroExecutionMode.Once,
                new MacroStep[] { new KeyTapStep(KeyboardKey.A) })
        }));
    }

    private static void AssertCodecFailure(
        string json,
        SettingsJsonErrorCode code,
        string path)
    {
        SettingsJsonReadResult result = SettingsJsonCodec.ReadV1OrV2OrV3(json);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Settings);
        SettingsJsonError error = Assert.Single(result.CodecErrors);
        Assert.Equal(code, error.Code);
        Assert.Equal(path, error.Path);
        Assert.Empty(result.ValidationErrors);
    }

    private static ApplicationSettingsCandidate Candidate(
        params MacroDefinitionCandidate[] macros) => new(macros);

    private static MacroDefinitionCandidate Macro(
        int id,
        bool enabled,
        MacroTrigger trigger,
        MacroExecutionMode mode,
        MacroSequence sequence) => new(
            Id(id), $"Macro {id}", enabled, trigger, mode, sequence);

    private static MacroDefinition Definition(
        int id,
        string name,
        MacroTrigger trigger,
        MacroExecutionMode mode,
        IEnumerable<MacroStep> steps) => new(
            Id(id), name, true, trigger, mode, new MacroSequence(steps));

    private static MacroSequence Key(KeyboardKey key) =>
        new(new MacroStep[] { new KeyTapStep(key) });

    private static MacroId Id(int value) =>
        new(new Guid(value, 0, 0, new byte[8]));
}
