using System.Text.Json;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class SettingsJsonCodecTests
{
    private const long MaximumSafeTimeSpanMilliseconds =
        long.MaxValue / TimeSpan.TicksPerMillisecond;

    public static TheoryData<string, object, string>
        VersionFailures => new()
        {
            {
                "{\"macro\":" + SettingsJsonTestSupport.MacroJson() + "}",
                SettingsJsonErrorCode.MissingRequiredProperty,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("\"1\""),
                SettingsJsonErrorCode.ExpectedInteger,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("1.0"),
                SettingsJsonErrorCode.ExpectedInteger,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("1e0"),
                SettingsJsonErrorCode.ExpectedInteger,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("0"),
                SettingsJsonErrorCode.UnsupportedVersion,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("2"),
                SettingsJsonErrorCode.UnsupportedVersion,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("null"),
                SettingsJsonErrorCode.ExpectedInteger,
                "version"
            },
            {
                SettingsJsonTestSupport.DocumentJson("{}"),
                SettingsJsonErrorCode.ExpectedInteger,
                "version"
            }
        };

    public static TheoryData<string, object, string>
        RootFailures => new()
        {
            { string.Empty, SettingsJsonErrorCode.InvalidJson, "json" },
            { "{", SettingsJsonErrorCode.InvalidJson, "json" },
            { "[]", SettingsJsonErrorCode.ExpectedObject, "json" },
            { "\"text\"", SettingsJsonErrorCode.ExpectedObject, "json" },
            { "42", SettingsJsonErrorCode.ExpectedObject, "json" },
            { "null", SettingsJsonErrorCode.ExpectedObject, "json" }
        };

    public static TheoryData<string> CommentsAndTrailingCommas
    {
        get
        {
            string macro = SettingsJsonTestSupport.MacroJson();
            return new TheoryData<string>
            {
                { "{\"version\":1,// comment\n\"macro\":" + macro + "}" },
                { "{\"version\":1,/* comment */\"macro\":" + macro + "}" },
                { "{\"version\":1,\"macro\":" + macro + ",}" },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson:
                                "[{\"type\":\"keyTap\",\"key\":\"M\"},]"))
                }
            };
        }
    }

    public static TheoryData<string, string> DuplicateProperties
    {
        get
        {
            string hotkey = SettingsJsonTestSupport.HotkeyJson();
            string steps = SettingsJsonTestSupport.KeyTapStepsJson();
            string macro = SettingsJsonTestSupport.MacroJson();
            string delaySteps =
                "[{\"type\":\"keyTap\",\"key\":\"M\"}," +
                "{\"type\":\"delay\",\"milliseconds\":250," +
                "\"milliseconds\":251}]";

            return new TheoryData<string, string>
            {
                {
                    "{\"version\":1,\"version\":1,\"macro\":" + macro + "}",
                    "version"
                },
                {
                    "{\"version\":1,\"macro\":" + macro +
                        ",\"macro\":" + macro + "}",
                    "macro"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"name\":\"A\",\"name\":\"B\"," +
                            "\"hotkey\":" + hotkey + ",\"steps\":" + steps + "}"),
                    "macro.name"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"name\":\"A\",\"hotkey\":" + hotkey +
                            ",\"hotkey\":" + hotkey + ",\"steps\":" + steps + "}"),
                    "macro.hotkey"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"name\":\"A\",\"hotkey\":" + hotkey +
                            ",\"steps\":" + steps + ",\"steps\":" + steps + "}"),
                    "macro.steps"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            hotkeyJson:
                                "{\"key\":\"F8\",\"key\":\"F9\"," +
                                "\"modifiers\":[]}")),
                    "macro.hotkey.key"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            hotkeyJson:
                                "{\"key\":\"F8\",\"modifiers\":[]," +
                                "\"modifiers\":[\"Control\"]}")),
                    "macro.hotkey.modifiers"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson:
                                "[{\"type\":\"keyTap\",\"type\":\"delay\"," +
                                "\"key\":\"M\"}]")),
                    "macro.steps[0].type"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson:
                                "[{\"type\":\"keyTap\",\"key\":\"M\"," +
                                "\"key\":\"N\"}]")),
                    "macro.steps[0].key"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson: delaySteps)),
                    "macro.steps[1].milliseconds"
                }
            };
        }
    }

    public static TheoryData<string, string> UnknownProperties
    {
        get
        {
            string hotkey = SettingsJsonTestSupport.HotkeyJson();
            string steps = SettingsJsonTestSupport.KeyTapStepsJson();
            return new TheoryData<string, string>
            {
                {
                    "{\"version\":1,\"macro\":" +
                        SettingsJsonTestSupport.MacroJson() + ",\"extra\":true}",
                    "extra"
                },
                {
                    "{\"version\":1,\"macro\":" +
                        SettingsJsonTestSupport.MacroJson() +
                        ",\"$type\":\"Some.Clr.Type\"}",
                    "$type"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"name\":\"A\",\"hotkey\":" + hotkey +
                            ",\"steps\":" + steps + ",\"extra\":1}"),
                    "macro.extra"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            hotkeyJson:
                                "{\"key\":\"F8\",\"modifiers\":[],\"extra\":1}")),
                    "macro.hotkey.extra"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson:
                                "[{\"type\":\"keyTap\",\"key\":\"M\"," +
                                "\"milliseconds\":1}]")),
                    "macro.steps[0].milliseconds"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson:
                                "[{\"type\":\"delay\",\"milliseconds\":1," +
                                "\"key\":\"M\"}]")),
                    "macro.steps[0].key"
                }
            };
        }
    }

    public static TheoryData<string, string> MissingRequiredFields
    {
        get
        {
            string hotkey = SettingsJsonTestSupport.HotkeyJson();
            string steps = SettingsJsonTestSupport.KeyTapStepsJson();
            return new TheoryData<string, string>
            {
                { "{\"macro\":" + SettingsJsonTestSupport.MacroJson() + "}", "version" },
                { "{\"version\":1}", "macro" },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"hotkey\":" + hotkey + ",\"steps\":" + steps + "}"),
                    "macro.name"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"name\":\"A\",\"steps\":" + steps + "}"),
                    "macro.hotkey"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: "{\"name\":\"A\",\"hotkey\":" + hotkey + "}"),
                    "macro.steps"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            hotkeyJson: "{\"modifiers\":[]}")),
                    "macro.hotkey.key"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            hotkeyJson: "{\"key\":\"F8\"}")),
                    "macro.hotkey.modifiers"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson: "[{\"key\":\"M\"}]")),
                    "macro.steps[0].type"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson: "[{\"type\":\"keyTap\"}]")),
                    "macro.steps[0].key"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson: "[{\"milliseconds\":1}]")),
                    "macro.steps[0].type"
                },
                {
                    SettingsJsonTestSupport.DocumentJson(
                        macroJson: SettingsJsonTestSupport.MacroJson(
                            stepsJson: "[{\"type\":\"delay\"}]")),
                    "macro.steps[0].milliseconds"
                }
            };
        }
    }

    public static TheoryData<string, object, string> WrongTypes =>
        new()
        {
            {
                SettingsJsonTestSupport.DocumentJson(macroJson: "[]"),
                SettingsJsonErrorCode.ExpectedObject,
                "macro"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(nameJson: "123")),
                SettingsJsonErrorCode.ExpectedString,
                "macro.name"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(hotkeyJson: "[]")),
                SettingsJsonErrorCode.ExpectedObject,
                "macro.hotkey"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(stepsJson: "{}")),
                SettingsJsonErrorCode.ExpectedArray,
                "macro.steps"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        hotkeyJson: SettingsJsonTestSupport.HotkeyJson(keyJson: "8"))),
                SettingsJsonErrorCode.ExpectedString,
                "macro.hotkey.key"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        hotkeyJson: SettingsJsonTestSupport.HotkeyJson(
                            modifiersJson: "\"Control\""))),
                SettingsJsonErrorCode.ExpectedArray,
                "macro.hotkey.modifiers"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        hotkeyJson: SettingsJsonTestSupport.HotkeyJson(
                            modifiersJson: "[1]"))),
                SettingsJsonErrorCode.ExpectedString,
                "macro.hotkey.modifiers[0]"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson: "[\"keyTap\"]")),
                SettingsJsonErrorCode.ExpectedObject,
                "macro.steps[0]"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson: "[{\"type\":1,\"key\":\"M\"}]")),
                SettingsJsonErrorCode.ExpectedString,
                "macro.steps[0].type"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson: "[{\"type\":\"keyTap\",\"key\":1}]")),
                SettingsJsonErrorCode.ExpectedString,
                "macro.steps[0].key"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson:
                            "[{\"type\":\"delay\",\"milliseconds\":\"250\"}]")),
                SettingsJsonErrorCode.ExpectedInteger,
                "macro.steps[0].milliseconds"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson:
                            "[{\"type\":\"keyTap\",\"key\":\"A\"}," +
                            "{\"type\":\"keyTap\",\"key\":\"B\"}," +
                            "{\"type\":1,\"key\":\"C\"}]")),
                SettingsJsonErrorCode.ExpectedString,
                "macro.steps[2].type"
            },
            {
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson:
                            "[{\"type\":\"keyTap\",\"key\":\"A\"}," +
                            "{\"type\":\"keyTap\",\"key\":\"B\"}," +
                            "{\"type\":\"keyTap\",\"key\":\"C\"}," +
                            "{\"type\":\"keyTap\",\"key\":\"D\"}," +
                            "{\"type\":\"delay\",\"milliseconds\":\"250\"}]")),
                SettingsJsonErrorCode.ExpectedInteger,
                "macro.steps[4].milliseconds"
            }
        };

    public static TheoryData<string, object> InvalidStepTypes =>
        new()
        {
            { "\"unknown\"", SettingsJsonErrorCode.UnknownStepType },
            { "\"KeyTap\"", SettingsJsonErrorCode.UnknownStepType },
            { "1", SettingsJsonErrorCode.ExpectedString },
            { "null", SettingsJsonErrorCode.ExpectedString }
        };

    public static TheoryData<string, object> InvalidDelayValues =>
        new()
        {
            { "\"250\"", SettingsJsonErrorCode.ExpectedInteger },
            { "250.0", SettingsJsonErrorCode.ExpectedInteger },
            { "250.5", SettingsJsonErrorCode.ExpectedInteger },
            { "25e1", SettingsJsonErrorCode.ExpectedInteger },
            { "-1", SettingsJsonErrorCode.DelayMillisecondsOutOfRange },
            { "9223372036854775808", SettingsJsonErrorCode.IntegerOutOfRange },
            {
                (MaximumSafeTimeSpanMilliseconds + 1).ToString(),
                SettingsJsonErrorCode.DelayMillisecondsOutOfRange
            }
        };

    [Fact]
    public void Read_RepresentativeValidV1ProducesExpectedSettings()
    {
        const string json =
            "{\"version\":1,\"macro\":{\"name\":\"Example\"," +
            "\"hotkey\":{\"key\":\"F8\",\"modifiers\":[\"Control\"]}," +
            "\"steps\":[{\"type\":\"keyTap\",\"key\":\"M\"}," +
            "{\"type\":\"delay\",\"milliseconds\":250}]}}";

        ApplicationSettings settings = SettingsJsonTestSupport.AssertSuccess(
            SettingsJsonCodec.Read(json));
        MacroDefinition macro =
            SettingsJsonTestSupport.AssertSingleMacro(settings);

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, macro.Id);
        Assert.True(macro.Enabled);
        Assert.Equal("Example", macro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F8, HotkeyModifiers.Control),
            macro.Hotkey);
        Assert.Collection(
            macro.Sequence.Steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.M), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(250)),
                step));
    }

    [Fact]
    public void WriteThenRead_PreservesRepresentativeSemantics()
    {
        ApplicationSettings original = SettingsJsonTestSupport.CreateSettings(
            name: "Round Trip",
            hotkey: new KeyboardHotkey(
                HotkeyKey.F8,
                HotkeyModifiers.Control | HotkeyModifiers.Alt),
            steps: new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.M),
                new DelayStep(TimeSpan.FromMilliseconds(250)),
                new KeyTapStep(KeyboardKey.ArrowUp)
            });

        string json = SettingsJsonCodec.Write(original);
        ApplicationSettings restored = SettingsJsonTestSupport.AssertSuccess(
            SettingsJsonCodec.Read(json));
        MacroDefinition originalMacro =
            SettingsJsonTestSupport.AssertSingleMacro(original);
        MacroDefinition restoredMacro =
            SettingsJsonTestSupport.AssertSingleMacro(restored);

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, originalMacro.Id);
        Assert.Equal(originalMacro.Id, restoredMacro.Id);
        Assert.True(originalMacro.Enabled);
        Assert.True(restoredMacro.Enabled);
        Assert.Equal(originalMacro.Name, restoredMacro.Name);
        Assert.Equal(originalMacro.Hotkey, restoredMacro.Hotkey);
        Assert.Equal(
            originalMacro.Sequence.Steps,
            restoredMacro.Sequence.Steps);
    }

    [Theory]
    [MemberData(nameof(VersionFailures))]
    public void Read_RejectsMissingWrongSyntaxAndUnsupportedVersions(
        string json,
        object expectedCode,
        string expectedPath)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            expectedPath);
    }

    [Theory]
    [MemberData(nameof(RootFailures))]
    public void Read_ContainsMalformedAndWrongRootTypeFailures(
        string json,
        object expectedCode,
        string expectedPath)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            expectedPath);
    }

    [Theory]
    [MemberData(nameof(CommentsAndTrailingCommas))]
    public void Read_RejectsCommentsAndTrailingCommas(string json)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            SettingsJsonErrorCode.InvalidJson,
            "json");
    }

    [Theory]
    [MemberData(nameof(DuplicateProperties))]
    public void Read_RejectsDuplicatePropertiesAtEveryObjectLevel(
        string json,
        string expectedPath)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            SettingsJsonErrorCode.DuplicateProperty,
            expectedPath);
    }

    [Theory]
    [MemberData(nameof(UnknownProperties))]
    public void Read_RejectsUnknownAndClrLikeProperties(
        string json,
        string expectedPath)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            SettingsJsonErrorCode.UnknownProperty,
            expectedPath);
    }

    [Theory]
    [MemberData(nameof(MissingRequiredFields))]
    public void Read_RejectsMissingRequiredFieldsAsCodecErrors(
        string json,
        string expectedPath)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            SettingsJsonErrorCode.MissingRequiredProperty,
            expectedPath);
    }

    [Theory]
    [MemberData(nameof(WrongTypes))]
    public void Read_RejectsRepresentativeWrongJsonTypes(
        string json,
        object expectedCode,
        string expectedPath)
    {
        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            expectedPath);
    }

    [Theory]
    [InlineData("\"keyTap\"")]
    [InlineData("\"delay\"")]
    public void StepDiscriminator_AcceptsCanonicalTypes(string typeJson)
    {
        string step = typeJson == "\"keyTap\""
            ? "{\"type\":\"keyTap\",\"key\":\"M\"}"
            : "{\"type\":\"delay\",\"milliseconds\":1}";
        string steps = typeJson == "\"keyTap\""
            ? "[" + step + "]"
            : "[{\"type\":\"keyTap\",\"key\":\"M\"}," + step + "]";
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(stepsJson: steps));

        SettingsJsonTestSupport.AssertSuccess(SettingsJsonCodec.Read(json));
    }

    [Theory]
    [MemberData(nameof(InvalidStepTypes))]
    public void StepDiscriminator_RejectsUnknownCaseNumericAndNull(
        string typeJson,
        object expectedCode)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                stepsJson: "[{\"type\":" + typeJson + ",\"key\":\"M\"}]"));

        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            "macro.steps[0].type");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60_000, true)]
    [InlineData(60_001, false)]
    public void DelayIntegerSyntax_ReachesSemanticValidation(
        int milliseconds,
        bool shouldSucceed)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                stepsJson: SettingsJsonTestSupport.DelayStepsJson(
                    milliseconds.ToString())));

        if (shouldSucceed)
        {
            ApplicationSettings settings = SettingsJsonTestSupport.AssertSuccess(
                SettingsJsonCodec.Read(json));
            MacroDefinition macro =
                SettingsJsonTestSupport.AssertSingleMacro(settings);
            DelayStep delay = Assert.IsType<DelayStep>(
                macro.Sequence.Steps[1]);
            Assert.Equal(
                TimeSpan.FromMilliseconds(milliseconds),
                delay.Duration);
        }
        else
        {
            SettingsJsonTestSupport.AssertValidationFailure(
                json,
                (
                    ConfigurationValidationErrorCode.DelayOutOfRange,
                    "macros[0].steps[1].duration"));
        }
    }

    [Theory]
    [MemberData(nameof(InvalidDelayValues))]
    public void DelayReader_RejectsNonIntegerNegativeAndOverflowValues(
        string millisecondsJson,
        object expectedCode)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                stepsJson: SettingsJsonTestSupport.DelayStepsJson(
                    millisecondsJson)));

        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            "macro.steps[1].milliseconds");
    }

    [Fact]
    public void StructurallyValidSemanticFailuresReachConfigurationValidator()
    {
        string oneHundredOneSteps = "[" + string.Join(
            ",",
            Enumerable.Repeat(
                "{\"type\":\"keyTap\",\"key\":\"M\"}",
                101)) + "]";

        var cases = new[]
        {
            (
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        nameJson: "\"   \"")),
                ConfigurationValidationErrorCode.MacroNameRequired,
                "macros[0].name"),
            (
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        hotkeyJson: SettingsJsonTestSupport.HotkeyJson(
                            keyJson: "\"A\""))),
                ConfigurationValidationErrorCode.ModifierlessLetterHotkeyNotAllowed,
                "macros[0].hotkey"),
            (
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson: SettingsJsonTestSupport.DelayStepsJson("0"))),
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[0].steps[1].duration"),
            (
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson: SettingsJsonTestSupport.DelayStepsJson("60001"))),
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[0].steps[1].duration"),
            (
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson: oneHundredOneSteps)),
                ConfigurationValidationErrorCode.MacroStepCountOutOfRange,
                "macros[0].steps"),
            (
                SettingsJsonTestSupport.DocumentJson(
                    macroJson: SettingsJsonTestSupport.MacroJson(
                        stepsJson:
                            "[{\"type\":\"delay\",\"milliseconds\":1}]")),
                ConfigurationValidationErrorCode.MacroMustContainKeyTap,
                "macros[0].steps")
        };

        foreach ((string json, ConfigurationValidationErrorCode code, string path)
            in cases)
        {
            SettingsJsonTestSupport.AssertValidationFailure(json, (code, path));
        }
    }

    [Fact]
    public void Read_PreservesRawNameForValidatorNormalization()
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                nameJson: "\"  Example  \""));

        ApplicationSettings settings = SettingsJsonTestSupport.AssertSuccess(
            SettingsJsonCodec.Read(json));

        Assert.Equal(
            "Example",
            SettingsJsonTestSupport.AssertSingleMacro(settings).Name);
    }

    [Fact]
    public void Writer_UsesCanonicalPropertyOrderTokensAndReadableIndentation()
    {
        ApplicationSettings settings = SettingsJsonTestSupport.CreateSettings(
            hotkey: new KeyboardHotkey(
                HotkeyKey.F8,
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));

        string json = SettingsJsonCodec.Write(settings);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement macro = root.GetProperty("macro");
        JsonElement hotkey = macro.GetProperty("hotkey");
        JsonElement[] steps = macro.GetProperty("steps").EnumerateArray().ToArray();

        Assert.Equal(new[] { "version", "macro" }, PropertyNames(root));
        Assert.Equal(new[] { "name", "hotkey", "steps" }, PropertyNames(macro));
        Assert.Equal(new[] { "key", "modifiers" }, PropertyNames(hotkey));
        Assert.Equal(new[] { "type", "key" }, PropertyNames(steps[0]));
        Assert.Equal(new[] { "type", "milliseconds" }, PropertyNames(steps[1]));
        Assert.Equal("F8", hotkey.GetProperty("key").GetString());
        Assert.Equal("keyTap", steps[0].GetProperty("type").GetString());
        Assert.Equal("M", steps[0].GetProperty("key").GetString());
        Assert.Equal("delay", steps[1].GetProperty("type").GetString());
        Assert.Contains('\n', json);
    }

    [Fact]
    public void Writer_RejectsUndefinedOutputKey()
    {
        ApplicationSettings settings = SettingsJsonTestSupport.CreateSettings(
            steps: new MacroStep[]
            {
                new KeyTapStep((KeyboardKey)int.MaxValue)
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SettingsJsonCodec.Write(settings));
        Assert.Contains("Output key", exception.Message);
    }

    [Fact]
    public void Writer_RejectsUnsupportedMacroStep()
    {
        ApplicationSettings settings = SettingsJsonTestSupport.CreateSettings(
            steps: new MacroStep[] { new UnsupportedTestStep() });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SettingsJsonCodec.Write(settings));
        Assert.Contains("Macro step type", exception.Message);
    }

    [Fact]
    public void Writer_RejectsDelayNotRepresentableAsWholeMilliseconds()
    {
        ApplicationSettings settings = SettingsJsonTestSupport.CreateSettings(
            steps: new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.M),
                new DelayStep(new TimeSpan(TimeSpan.TicksPerMillisecond + 1))
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SettingsJsonCodec.Write(settings));
        Assert.Contains("whole-millisecond", exception.Message);
    }

    [Fact]
    public void Writer_RejectsSettingsWithoutMacros()
    {
        var settings = new ApplicationSettings(
            Array.Empty<MacroDefinition>());

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => SettingsJsonCodec.Write(settings));

        Assert.Contains("requires exactly one macro", exception.Message);
        Assert.Empty(settings.Macros);
    }

    [Fact]
    public void Writer_RejectsMultipleMacrosWithoutSerializingFirst()
    {
        MacroDefinition first = SettingsJsonTestSupport.AssertSingleMacro(
            SettingsJsonTestSupport.CreateSettings(name: "First"));
        var second = new MacroDefinition(
            new MacroId(new Guid("22222222-2222-2222-2222-222222222222")),
            "Second",
            true,
            new KeyboardHotkey(HotkeyKey.F9),
            first.Sequence);
        var settings = new ApplicationSettings(new[] { first, second });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => SettingsJsonCodec.Write(settings));

        Assert.Contains("requires exactly one macro", exception.Message);
        Assert.Equal(
            new[] { "First", "Second" },
            settings.Macros.Select(macro => macro.Name));
    }

    [Fact]
    public void Writer_RejectsSoleDisabledLegacyMacro()
    {
        MacroDefinition enabled = SettingsJsonTestSupport.AssertSingleMacro(
            SettingsJsonTestSupport.CreateSettings());
        var disabled = new MacroDefinition(
            enabled.Id,
            enabled.Name,
            false,
            enabled.Hotkey,
            enabled.Sequence);
        var settings = new ApplicationSettings(new[] { disabled });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => SettingsJsonCodec.Write(settings));

        Assert.Contains("only macro to be enabled", exception.Message);
        Assert.False(Assert.Single(settings.Macros).Enabled);
    }

    [Fact]
    public void Writer_RejectsSoleEnabledMacroWithNonLegacyMacroId()
    {
        MacroDefinition legacyMacro =
            SettingsJsonTestSupport.AssertSingleMacro(
                SettingsJsonTestSupport.CreateSettings());
        var nonLegacyId = new MacroId(
            new Guid("11111111-1111-1111-1111-111111111111"));
        var settings = new ApplicationSettings(
            new[]
            {
                new MacroDefinition(
                    nonLegacyId,
                    legacyMacro.Name,
                    true,
                    legacyMacro.Hotkey,
                    legacyMacro.Sequence)
            });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => SettingsJsonCodec.Write(settings));

        Assert.Contains("version 1 cannot preserve macro ID", exception.Message);
        Assert.Contains(nonLegacyId.ToString(), exception.Message);
        Assert.Equal(
            nonLegacyId,
            SettingsJsonTestSupport.AssertSingleMacro(settings).Id);
    }

    [Fact]
    public void Read_NullThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => SettingsJsonCodec.Read(null!));

        Assert.Equal("json", exception.ParamName);
    }

    [Fact]
    public void Write_NullThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => SettingsJsonCodec.Write(null!));

        Assert.Equal("settings", exception.ParamName);
    }

    private static string[] PropertyNames(JsonElement element)
    {
        return element.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private sealed record UnsupportedTestStep : MacroStep;
}
