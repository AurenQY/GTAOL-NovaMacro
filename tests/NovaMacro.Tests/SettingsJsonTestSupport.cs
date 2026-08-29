using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal static class SettingsJsonTestSupport
{
    internal static string ValidJson => DocumentJson();

    internal static string DocumentJson(
        string versionJson = "1",
        string? macroJson = null)
    {
        return "{\"version\":" + versionJson +
            ",\"macro\":" + (macroJson ?? MacroJson()) + "}";
    }

    internal static string MacroJson(
        string nameJson = "\"Example\"",
        string? hotkeyJson = null,
        string? stepsJson = null)
    {
        return "{\"name\":" + nameJson +
            ",\"hotkey\":" + (hotkeyJson ?? HotkeyJson()) +
            ",\"steps\":" + (stepsJson ?? KeyTapStepsJson()) + "}";
    }

    internal static string HotkeyJson(
        string keyJson = "\"F8\"",
        string modifiersJson = "[]")
    {
        return "{\"key\":" + keyJson +
            ",\"modifiers\":" + modifiersJson + "}";
    }

    internal static string KeyTapStepsJson(string keyJson = "\"M\"")
    {
        return "[{\"type\":\"keyTap\",\"key\":" + keyJson + "}]";
    }

    internal static string DelayStepsJson(string millisecondsJson)
    {
        return "[{\"type\":\"keyTap\",\"key\":\"M\"}," +
            "{\"type\":\"delay\",\"milliseconds\":" +
            millisecondsJson + "}]";
    }

    internal static ApplicationSettings CreateSettings(
        string name = "Example",
        KeyboardHotkey? hotkey = null,
        IEnumerable<MacroStep>? steps = null)
    {
        return new ApplicationSettings(
            new MacroDefinition(
                name,
                hotkey ?? new KeyboardHotkey(HotkeyKey.F8),
                new MacroSequence(
                    steps ?? new MacroStep[]
                    {
                        new KeyTapStep(KeyboardKey.M),
                        new DelayStep(TimeSpan.FromMilliseconds(250))
                    })));
    }

    internal static ApplicationSettings AssertSuccess(
        SettingsJsonReadResult result)
    {
        Assert.True(result.IsSuccess);
        ApplicationSettings settings = Assert.IsType<ApplicationSettings>(
            result.Settings);
        Assert.Empty(result.CodecErrors);
        Assert.Empty(result.ValidationErrors);
        return settings;
    }

    internal static SettingsJsonError AssertCodecFailure(
        string json,
        SettingsJsonErrorCode expectedCode,
        string expectedPath)
    {
        SettingsJsonReadResult result = SettingsJsonCodec.Read(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Settings);
        SettingsJsonError error = Assert.Single(result.CodecErrors);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedPath, error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Empty(result.ValidationErrors);
        return error;
    }

    internal static SettingsJsonReadResult AssertValidationFailure(
        string json,
        params (ConfigurationValidationErrorCode Code, string Path)[] expected)
    {
        SettingsJsonReadResult result = SettingsJsonCodec.Read(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Settings);
        Assert.Empty(result.CodecErrors);
        Assert.NotEmpty(result.ValidationErrors);
        Assert.All(
            result.ValidationErrors,
            error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
        Assert.Equal(
            expected,
            result.ValidationErrors.Select(error => (error.Code, error.Path)));
        return result;
    }
}
