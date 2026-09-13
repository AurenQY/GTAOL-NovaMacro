using System.Text.Json;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class SettingsJsonV2CodecTests
{
    [Fact]
    public void WriteV2ThenReadV1OrV2_RoundTripsEmptySettings()
    {
        var original = new ApplicationSettings(Array.Empty<MacroDefinition>());

        string json = SettingsJsonCodec.WriteV2(original);
        ApplicationSettings restored = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(json));

        Assert.Empty(restored.Macros);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            new[] { "version", "macros" },
            PropertyNames(document.RootElement));
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void WriteV2ThenReadV1OrV2_RoundTripsOrderedMultiMacroSettings()
    {
        ApplicationSettings original = CreateMultiMacroSettings();

        ApplicationSettings restored = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(SettingsJsonCodec.WriteV2(original)));

        AssertMacrosEqual(original.Macros, restored.Macros);
        Assert.Equal(
            new[] { "First", "Second", "Third" },
            restored.Macros.Select(macro => macro.Name));
    }

    [Fact]
    public void WriteV2ThenReadV1OrV2_PreservesNonLegacyMacroId()
    {
        MacroDefinition original = CreateMacro(
            "11111111-1111-1111-1111-111111111111",
            "Non legacy",
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            new MacroStep[] { new KeyTapStep(KeyboardKey.M) });
        var settings = new ApplicationSettings(new[] { original });

        ApplicationSettings restored = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(SettingsJsonCodec.WriteV2(settings)));

        Assert.Equal(original.Id, Assert.Single(restored.Macros).Id);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, original.Id);
    }

    [Fact]
    public void WriteV2ThenReadV1OrV2_PreservesDisabledMacro()
    {
        MacroDefinition original = CreateMacro(
            "22222222-2222-2222-2222-222222222222",
            "Disabled",
            false,
            new KeyboardHotkey(HotkeyKey.F8),
            new MacroStep[] { new KeyTapStep(KeyboardKey.N) });
        var settings = new ApplicationSettings(new[] { original });

        ApplicationSettings restored = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(SettingsJsonCodec.WriteV2(settings)));

        Assert.False(Assert.Single(restored.Macros).Enabled);
    }

    [Fact]
    public void WriteV2ThenReadV1OrV2_PreservesKeyTapDelayKeyTapOrder()
    {
        var original = new ApplicationSettings(new[] { CreateMacro(
            "12121212-1212-1212-1212-121212121212",
            "Ordered steps",
            true,
            new KeyboardHotkey(HotkeyKey.F8, HotkeyModifiers.Control),
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.Digit0),
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                new KeyTapStep(KeyboardKey.ArrowUp)
            }) });

        ApplicationSettings restored = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(SettingsJsonCodec.WriteV2(original)));

        Assert.Equal(
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.Digit0),
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                new KeyTapStep(KeyboardKey.ArrowUp)
            },
            Assert.Single(restored.Macros).Sequence.Steps);
    }

    [Fact]
    public void WriteV2_UsesDeterministicPropertyAndMacroOrder()
    {
        ApplicationSettings settings = CreateMultiMacroSettings();

        string first = SettingsJsonCodec.WriteV2(settings);
        string second = SettingsJsonCodec.WriteV2(settings);

        Assert.Equal(first, second);
        using JsonDocument document = JsonDocument.Parse(first);
        JsonElement[] macros = document.RootElement
            .GetProperty("macros")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(
            new[] { "id", "name", "enabled", "hotkey", "steps" },
            PropertyNames(macros[0]));
        Assert.Equal(
            new[] { "First", "Second", "Third" },
            macros.Select(macro => macro.GetProperty("name").GetString()));
        Assert.Equal(
            new[]
            {
                "11111111-1111-1111-1111-111111111111",
                "22222222-2222-2222-2222-222222222222",
                "33333333-3333-3333-3333-333333333333"
            },
            macros.Select(macro => macro.GetProperty("id").GetString()));
    }

    [Fact]
    public void ReadV1OrV2_RejectsUnknownV2Property()
    {
        string json = V2DocumentJson(
            "[" + V2MacroJson(suffix: ",\"extra\":true") + "]");

        AssertTransitionCodecFailure(
            json,
            SettingsJsonErrorCode.UnknownProperty,
            "macros[0].extra");
    }

    [Fact]
    public void ReadV1OrV2_RejectsDuplicateV2Property()
    {
        const string json = "{\"version\":2,\"version\":2,\"macros\":[]}";

        AssertTransitionCodecFailure(
            json,
            SettingsJsonErrorCode.DuplicateProperty,
            "version");
    }

    [Fact]
    public void ReadV1OrV2_RejectsMissingV2MacroProperty()
    {
        string json = V2DocumentJson(
            "[" + V2MacroJson(includeEnabled: false) + "]");

        AssertTransitionCodecFailure(
            json,
            SettingsJsonErrorCode.MissingRequiredProperty,
            "macros[0].enabled");
    }

    [Fact]
    public void ReadV1OrV2_RejectsNonArrayMacros()
    {
        const string json = "{\"version\":2,\"macros\":{}}";

        AssertTransitionCodecFailure(
            json,
            SettingsJsonErrorCode.ExpectedArray,
            "macros");
    }

    [Fact]
    public void ReadV1OrV2_RejectsNonBooleanEnabled()
    {
        string json = V2DocumentJson(
            "[" + V2MacroJson(enabledJson: "\"true\"") + "]");

        AssertTransitionCodecFailure(
            json,
            SettingsJsonErrorCode.ExpectedBoolean,
            "macros[0].enabled");
    }

    [Theory]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    public void ReadV1OrV2_RejectsInvalidOrEmptyMacroId(string idJson)
    {
        string json = V2DocumentJson("[" + V2MacroJson(idJson) + "]");

        AssertTransitionCodecFailure(
            json,
            SettingsJsonErrorCode.InvalidMacroId,
            "macros[0].id");
    }

    [Fact]
    public void ReadV1OrV2_UsesCanonicalValidationForDuplicateMacroIds()
    {
        const string id = "\"11111111-1111-1111-1111-111111111111\"";
        string json = V2DocumentJson(
            "[" + V2MacroJson(id, nameJson: "\"First\"") + "," +
            V2MacroJson(
                id,
                nameJson: "\"Second\"",
                hotkeyJson: "{\"key\":\"F9\",\"modifiers\":[]}") + "]");

        AssertTransitionValidationFailure(
            json,
            ConfigurationValidationErrorCode.DuplicateMacroId,
            "macros[1].id");
    }

    [Fact]
    public void ReadV1OrV2_UsesCanonicalValidationForDuplicateEnabledHotkeys()
    {
        string json = V2DocumentJson(
            "[" + V2MacroJson(
                "\"11111111-1111-1111-1111-111111111111\"") + "," +
            V2MacroJson(
                "\"22222222-2222-2222-2222-222222222222\"",
                nameJson: "\"Second\"") + "]");

        AssertTransitionValidationFailure(
            json,
            ConfigurationValidationErrorCode.DuplicateEnabledTrigger,
            "macros[1].trigger");
    }

    [Fact]
    public void ReadV1OrV2_AllowsDisabledMacrosToShareEnabledHotkeys()
    {
        string json = V2DocumentJson(
            "[" + V2MacroJson(
                "\"11111111-1111-1111-1111-111111111111\"") + "," +
            V2MacroJson(
                "\"22222222-2222-2222-2222-222222222222\"",
                nameJson: "\"Disabled\"",
                enabledJson: "false") + "]");

        ApplicationSettings settings = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(json));

        Assert.Equal(new[] { true, false }, settings.Macros.Select(macro => macro.Enabled));
        Assert.Equal(settings.Macros[0].GetKeyboardHotkey(), settings.Macros[1].GetKeyboardHotkey());
    }

    [Fact]
    public void ReadV1OrV2_MigratesV1ToCanonicalLegacyMacro()
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                nameJson: "\"Migrated\"",
                hotkeyJson: "{\"key\":\"F9\",\"modifiers\":[\"Control\"]}",
                stepsJson: "[{\"type\":\"keyTap\",\"key\":\"N\"}]"));

        ApplicationSettings settings = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(json));
        MacroDefinition macro = Assert.Single(settings.Macros);

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, macro.Id);
        Assert.True(macro.Enabled);
        Assert.Equal("Migrated", macro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F9, HotkeyModifiers.Control),
            macro.GetKeyboardHotkey());
        Assert.Equal(
            new MacroStep[] { new KeyTapStep(KeyboardKey.N) },
            macro.Sequence.Steps);
    }

    [Fact]
    public void CurrentV1Reader_RemainsV1OnlyForV2Json()
    {
        string v2Json = SettingsJsonCodec.WriteV2(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        SettingsJsonReadResult result = SettingsJsonCodec.Read(v2Json);

        Assert.False(result.IsSuccess);
        SettingsJsonError error = Assert.Single(result.CodecErrors);
        Assert.Equal(SettingsJsonErrorCode.UnknownProperty, error.Code);
        Assert.Equal("macros", error.Path);
    }

    [Fact]
    public void ReadV1OrV2_AcceptsDFormatMacroIdAndWriterEmitsCanonicalDFormat()
    {
        const string id = "12345678-1234-1234-1234-123456789abc";
        string json = V2DocumentJson("[" + V2MacroJson("\"" + id + "\"") + "]");

        ApplicationSettings settings = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(json));
        string written = SettingsJsonCodec.WriteV2(settings);

        Assert.Equal(new MacroId(new Guid(id)), Assert.Single(settings.Macros).Id);
        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal(id, document.RootElement
            .GetProperty("macros")[0]
            .GetProperty("id")
            .GetString());
    }

    [Theory]
    [InlineData("\"11111111111111111111111111111111\"")]
    [InlineData("\"{11111111-1111-1111-1111-111111111111}\"")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("null")]
    public void ReadV1OrV2_RejectsNonDFormatOrNonStringMacroId(string idJson)
    {
        string json = V2DocumentJson("[" + V2MacroJson(idJson) + "]");
        SettingsJsonErrorCode code = idJson == "null"
            ? SettingsJsonErrorCode.ExpectedString
            : SettingsJsonErrorCode.InvalidMacroId;

        AssertTransitionCodecFailure(json, code, "macros[0].id");
    }

    [Fact]
    public void ReadV1OrV2_RejectsStrictV2TopLevelSchemaViolations()
    {
        var cases = new (string Json, SettingsJsonErrorCode Code, string Path)[]
        {
            ("{\"version\":2,\"macros\":[],\"extra\":true}", SettingsJsonErrorCode.UnknownProperty, "extra"),
            ("{\"macros\":[]}", SettingsJsonErrorCode.MissingRequiredProperty, "version"),
            ("{\"version\":2}", SettingsJsonErrorCode.MissingRequiredProperty, "macros"),
            ("{\"version\":2,\"version\":2,\"macros\":[]}", SettingsJsonErrorCode.DuplicateProperty, "version"),
            ("{\"version\":2,\"macros\":[],\"macros\":[]}", SettingsJsonErrorCode.DuplicateProperty, "macros"),
            ("{\"version\":\"2\",\"macros\":[]}", SettingsJsonErrorCode.ExpectedInteger, "version"),
            ("{\"version\":0,\"macros\":[]}", SettingsJsonErrorCode.UnsupportedVersion, "version"),
            ("{\"version\":3,\"macros\":[]}", SettingsJsonErrorCode.UnsupportedVersion, "version")
        };

        foreach ((string json, SettingsJsonErrorCode code, string path) in cases)
        {
            AssertTransitionCodecFailure(json, code, path);
        }
    }

    [Fact]
    public void ReadV1OrV2_RejectsStrictV2MacroSchemaViolations()
    {
        var cases = new (string Macro, SettingsJsonErrorCode Code, string Path)[]
        {
            ("{\"name\":\"Example\",\"enabled\":true,\"hotkey\":{\"key\":\"F8\",\"modifiers\":[]},\"steps\":[]}", SettingsJsonErrorCode.MissingRequiredProperty, "macros[0].id"),
            ("{\"id\":\"11111111-1111-1111-1111-111111111111\",\"enabled\":true,\"hotkey\":{\"key\":\"F8\",\"modifiers\":[]},\"steps\":[]}", SettingsJsonErrorCode.MissingRequiredProperty, "macros[0].name"),
            ("{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Example\",\"hotkey\":{\"key\":\"F8\",\"modifiers\":[]},\"steps\":[]}", SettingsJsonErrorCode.MissingRequiredProperty, "macros[0].enabled"),
            ("{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Example\",\"enabled\":true,\"steps\":[]}", SettingsJsonErrorCode.MissingRequiredProperty, "macros[0].hotkey"),
            ("{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Example\",\"enabled\":true,\"hotkey\":{\"key\":\"F8\",\"modifiers\":[]}}", SettingsJsonErrorCode.MissingRequiredProperty, "macros[0].steps"),
            (V2MacroJson(suffix: ",\"extra\":true"), SettingsJsonErrorCode.UnknownProperty, "macros[0].extra"),
            (V2MacroJson().Replace("\"id\":", "\"id\":\"11111111-1111-1111-1111-111111111111\",\"id\":", StringComparison.Ordinal), SettingsJsonErrorCode.DuplicateProperty, "macros[0].id"),
            (V2MacroJson().Replace("\"enabled\":true", "\"enabled\":true,\"enabled\":false", StringComparison.Ordinal), SettingsJsonErrorCode.DuplicateProperty, "macros[0].enabled"),
            (V2MacroJson(nameJson: "null"), SettingsJsonErrorCode.ExpectedString, "macros[0].name"),
            (V2MacroJson(hotkeyJson: "[]"), SettingsJsonErrorCode.ExpectedObject, "macros[0].hotkey"),
            (V2MacroJson(stepsJson: "null"), SettingsJsonErrorCode.ExpectedArray, "macros[0].steps")
        };

        foreach ((string macro, SettingsJsonErrorCode code, string path) in cases)
        {
            AssertTransitionCodecFailure(V2DocumentJson("[" + macro + "]"), code, path);
        }
    }

    [Fact]
    public void ReadV1OrV2_UsesV1NestedParsingRulesWithIndexedV2Paths()
    {
        var cases = new (string Json, SettingsJsonErrorCode Code, string Path)[]
        {
            (V2DocumentJson("[" + V2MacroJson(hotkeyJson: "{\"key\":\"Digit0\",\"modifiers\":[]}") + "]"), SettingsJsonErrorCode.UnknownHotkeyKey, "macros[0].hotkey.key"),
            (V2DocumentJson("[" + V2MacroJson(hotkeyJson: "{\"key\":\"F8\",\"key\":\"F9\",\"modifiers\":[]}") + "]"), SettingsJsonErrorCode.DuplicateProperty, "macros[0].hotkey.key"),
            (V2DocumentJson("[" + V2MacroJson(hotkeyJson: "{\"key\":\"F8\",\"modifiers\":[],\"extra\":true}") + "]"), SettingsJsonErrorCode.UnknownProperty, "macros[0].hotkey.extra"),
            (V2DocumentJson("[" + V2MacroJson(stepsJson: "[{\"type\":\"keyTap\",\"key\":\"M\"},{\"type\":\"delay\",\"milliseconds\":\"150\"}]") + "]"), SettingsJsonErrorCode.ExpectedInteger, "macros[0].steps[1].milliseconds"),
            (V2DocumentJson("[" + V2MacroJson(stepsJson: "[{\"type\":\"keyTap\",\"key\":\"M\"},null]") + "]"), SettingsJsonErrorCode.ExpectedObject, "macros[0].steps[1]"),
            (V2DocumentJson("[" + V2MacroJson(stepsJson: "[{\"type\":\"keyTap\",\"key\":\"M\"},{\"type\":\"unsupported\"}]") + "]"), SettingsJsonErrorCode.UnknownStepType, "macros[0].steps[1].type"),
            (V2DocumentJson("[" + V2MacroJson(stepsJson: "[{\"type\":\"keyTap\",\"key\":\"M\"},{\"type\":\"keyTap\",\"key\":\"F8\"}]") + "]"), SettingsJsonErrorCode.UnknownOutputKey, "macros[0].steps[1].key")
        };

        foreach ((string json, SettingsJsonErrorCode code, string path) in cases)
        {
            AssertTransitionCodecFailure(json, code, path);
        }
    }

    [Theory]
    [InlineData("{\"version\":2,/* comment */\"macros\":[]}")]
    [InlineData("{\"version\":2,\"macros\":[],}")]
    public void ReadV1OrV2_RejectsCommentsAndTrailingCommas(string json)
    {
        AssertTransitionCodecFailure(json, SettingsJsonErrorCode.InvalidJson, "json");
    }

    [Fact]
    public void ReadV1OrV2_UsesCanonicalCollectionValidationForMoreThan32Macros()
    {
        string macros = string.Join(
            ",",
            Enumerable.Range(1, 33).Select(index => V2MacroJson(
                idJson: "\"00000000-0000-0000-0000-" + index.ToString("D12", System.Globalization.CultureInfo.InvariantCulture) + "\"",
                nameJson: "\"Macro " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"",
                enabledJson: "false")));

        AssertTransitionValidationFailure(
            V2DocumentJson("[" + macros + "]"),
            ConfigurationValidationErrorCode.MacroCountOutOfRange,
            "macros");
    }

    [Fact]
    public void ReadV1OrV2_AllowsDisabledDuplicateHotkeysAndDuplicateNames()
    {
        string json = V2DocumentJson("[" + V2MacroJson(
            "\"11111111-1111-1111-1111-111111111111\"",
            nameJson: "\"Same\"",
            enabledJson: "false") + "," + V2MacroJson(
            "\"22222222-2222-2222-2222-222222222222\"",
            nameJson: "\"Same\"",
            enabledJson: "false") + "]");

        ApplicationSettings settings = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(json));

        Assert.Equal(2, settings.Macros.Count);
        Assert.All(settings.Macros, macro => Assert.False(macro.Enabled));
        Assert.Equal(settings.Macros[0].GetKeyboardHotkey(), settings.Macros[1].GetKeyboardHotkey());
        Assert.Equal(settings.Macros[0].Name, settings.Macros[1].Name);
    }

    [Fact]
    public void ReadV1OrV2_MigratesV1WithoutRelaxingItsSchema()
    {
        const string malformedV1 =
            "{\"version\":1,\"macro\":{\"name\":\"Example\",\"hotkey\":{\"key\":\"F8\",\"modifiers\":[]},\"steps\":[]},\"macros\":[]}";

        AssertTransitionCodecFailure(
            malformedV1,
            SettingsJsonErrorCode.UnknownProperty,
            "macros");

        ApplicationSettings migrated = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(SettingsJsonTestSupport.DocumentJson()));

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, Assert.Single(migrated.Macros).Id);
    }

    [Fact]
    public void ReadV1OrV2_MigratesV1InMemoryWithoutWritingOrCreatingFiles()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.GetPath("settings.json");
        string v1Json = SettingsJsonTestSupport.DocumentJson();
        File.WriteAllText(path, v1Json);
        byte[] before = File.ReadAllBytes(path);

        ApplicationSettings migrated = AssertTransitionSuccess(
            SettingsJsonCodec.ReadV1OrV2(File.ReadAllText(path)));

        Assert.Single(migrated.Macros);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.EnumerateFiles(temporary.Path));
    }

    [Fact]
    public void WriteV2_AcceptsCanonicalShapesExcludedByV1Compatibility()
    {
        var settingsCases = new[]
        {
            new ApplicationSettings(Array.Empty<MacroDefinition>()),
            new ApplicationSettings(new[] { CreateMacro(
                "11111111-1111-1111-1111-111111111111",
                "Disabled",
                false,
                new KeyboardHotkey(HotkeyKey.F8),
                new MacroStep[] { new KeyTapStep(KeyboardKey.M) }) }),
            CreateMultiMacroSettings()
        };

        foreach (ApplicationSettings settings in settingsCases)
        {
            ApplicationSettings restored = AssertTransitionSuccess(
                SettingsJsonCodec.ReadV1OrV2(SettingsJsonCodec.WriteV2(settings)));
            AssertMacrosEqual(settings.Macros, restored.Macros);
        }
    }

    [Fact]
    public void WriteV2_RejectsUnsupportedMacroStep()
    {
        var settings = new ApplicationSettings(new[] { CreateMacro(
            "11111111-1111-1111-1111-111111111111",
            "Unsupported",
            true,
            new KeyboardHotkey(HotkeyKey.F8),
            new MacroStep[] { new UnsupportedV2TestStep() }) });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SettingsJsonCodec.WriteV2(settings));

        Assert.Contains("Macro step type", exception.Message);
    }

    private static ApplicationSettings CreateMultiMacroSettings()
    {
        return new ApplicationSettings(
            new[]
            {
                CreateMacro(
                    "11111111-1111-1111-1111-111111111111",
                    "First",
                    true,
                    new KeyboardHotkey(HotkeyKey.F8),
                    new MacroStep[] { new KeyTapStep(KeyboardKey.M) }),
                CreateMacro(
                    "22222222-2222-2222-2222-222222222222",
                    "Second",
                    false,
                    new KeyboardHotkey(HotkeyKey.F9, HotkeyModifiers.Control),
                    new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.N),
                        new DelayStep(TimeSpan.FromMilliseconds(250))
                    }),
                CreateMacro(
                    "33333333-3333-3333-3333-333333333333",
                    "Third",
                    true,
                    new KeyboardHotkey(HotkeyKey.F10, HotkeyModifiers.Alt),
                    new MacroStep[]
                    {
                        new DelayStep(TimeSpan.FromMilliseconds(100)),
                        new KeyTapStep(KeyboardKey.ArrowUp)
                    })
            });
    }

    private static MacroDefinition CreateMacro(
        string id,
        string name,
        bool enabled,
        KeyboardHotkey hotkey,
        IEnumerable<MacroStep> steps)
    {
        return new MacroDefinition(
            new MacroId(new Guid(id)),
            name,
            enabled,
            hotkey,
            new MacroSequence(steps));
    }

    private static void AssertMacrosEqual(
        IReadOnlyList<MacroDefinition> expected,
        IReadOnlyList<MacroDefinition> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Enabled, actual[index].Enabled);
            Assert.Equal(expected[index].GetKeyboardHotkey(), actual[index].GetKeyboardHotkey());
            Assert.Equal(expected[index].Sequence.Steps, actual[index].Sequence.Steps);
        }
    }

    private static ApplicationSettings AssertTransitionSuccess(
        SettingsJsonReadResult result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.CodecErrors);
        Assert.Empty(result.ValidationErrors);
        return Assert.IsType<ApplicationSettings>(result.Settings);
    }

    private static void AssertTransitionCodecFailure(
        string json,
        SettingsJsonErrorCode expectedCode,
        string expectedPath)
    {
        SettingsJsonReadResult result = SettingsJsonCodec.ReadV1OrV2(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Settings);
        SettingsJsonError error = Assert.Single(result.CodecErrors);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedPath, error.Path);
        Assert.Empty(result.ValidationErrors);
    }

    private static void AssertTransitionValidationFailure(
        string json,
        ConfigurationValidationErrorCode expectedCode,
        string expectedPath)
    {
        SettingsJsonReadResult result = SettingsJsonCodec.ReadV1OrV2(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Settings);
        Assert.Empty(result.CodecErrors);
        ConfigurationValidationError error = Assert.Single(result.ValidationErrors);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedPath, error.Path);
    }

    private static string V2DocumentJson(string macrosJson)
    {
        return "{\"version\":2,\"macros\":" + macrosJson + "}";
    }

    private static string V2MacroJson(
        string idJson = "\"11111111-1111-1111-1111-111111111111\"",
        string nameJson = "\"Example\"",
        string enabledJson = "true",
        string hotkeyJson = "{\"key\":\"F8\",\"modifiers\":[]}",
        string stepsJson = "[{\"type\":\"keyTap\",\"key\":\"M\"}]",
        bool includeEnabled = true,
        string suffix = "")
    {
        string enabled = includeEnabled ? ",\"enabled\":" + enabledJson : string.Empty;
        return "{\"id\":" + idJson +
            ",\"name\":" + nameJson +
            enabled +
            ",\"hotkey\":" + hotkeyJson +
            ",\"steps\":" + stepsJson +
            suffix + "}";
    }

    private static string[] PropertyNames(JsonElement element)
    {
        return element.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private sealed record UnsupportedV2TestStep : MacroStep;
}
