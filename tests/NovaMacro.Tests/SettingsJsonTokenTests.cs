using System.Text.Json;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class SettingsJsonTokenTests
{
    public static TheoryData<HotkeyKey, string> HotkeyTokens => new()
    {
        { HotkeyKey.A, "A" },
        { HotkeyKey.B, "B" },
        { HotkeyKey.C, "C" },
        { HotkeyKey.D, "D" },
        { HotkeyKey.E, "E" },
        { HotkeyKey.F, "F" },
        { HotkeyKey.G, "G" },
        { HotkeyKey.H, "H" },
        { HotkeyKey.I, "I" },
        { HotkeyKey.J, "J" },
        { HotkeyKey.K, "K" },
        { HotkeyKey.L, "L" },
        { HotkeyKey.M, "M" },
        { HotkeyKey.N, "N" },
        { HotkeyKey.O, "O" },
        { HotkeyKey.P, "P" },
        { HotkeyKey.Q, "Q" },
        { HotkeyKey.R, "R" },
        { HotkeyKey.S, "S" },
        { HotkeyKey.T, "T" },
        { HotkeyKey.U, "U" },
        { HotkeyKey.V, "V" },
        { HotkeyKey.W, "W" },
        { HotkeyKey.X, "X" },
        { HotkeyKey.Y, "Y" },
        { HotkeyKey.Z, "Z" },
        { HotkeyKey.F1, "F1" },
        { HotkeyKey.F2, "F2" },
        { HotkeyKey.F3, "F3" },
        { HotkeyKey.F4, "F4" },
        { HotkeyKey.F5, "F5" },
        { HotkeyKey.F6, "F6" },
        { HotkeyKey.F7, "F7" },
        { HotkeyKey.F8, "F8" },
        { HotkeyKey.F9, "F9" },
        { HotkeyKey.F10, "F10" },
        { HotkeyKey.F11, "F11" }
    };

    public static TheoryData<KeyboardKey, string> OutputKeyTokens => new()
    {
        { KeyboardKey.A, "A" },
        { KeyboardKey.B, "B" },
        { KeyboardKey.C, "C" },
        { KeyboardKey.D, "D" },
        { KeyboardKey.E, "E" },
        { KeyboardKey.F, "F" },
        { KeyboardKey.G, "G" },
        { KeyboardKey.H, "H" },
        { KeyboardKey.I, "I" },
        { KeyboardKey.J, "J" },
        { KeyboardKey.K, "K" },
        { KeyboardKey.L, "L" },
        { KeyboardKey.M, "M" },
        { KeyboardKey.N, "N" },
        { KeyboardKey.O, "O" },
        { KeyboardKey.P, "P" },
        { KeyboardKey.Q, "Q" },
        { KeyboardKey.R, "R" },
        { KeyboardKey.S, "S" },
        { KeyboardKey.T, "T" },
        { KeyboardKey.U, "U" },
        { KeyboardKey.V, "V" },
        { KeyboardKey.W, "W" },
        { KeyboardKey.X, "X" },
        { KeyboardKey.Y, "Y" },
        { KeyboardKey.Z, "Z" },
        { KeyboardKey.Digit0, "Digit0" },
        { KeyboardKey.Digit1, "Digit1" },
        { KeyboardKey.Digit2, "Digit2" },
        { KeyboardKey.Digit3, "Digit3" },
        { KeyboardKey.Digit4, "Digit4" },
        { KeyboardKey.Digit5, "Digit5" },
        { KeyboardKey.Digit6, "Digit6" },
        { KeyboardKey.Digit7, "Digit7" },
        { KeyboardKey.Digit8, "Digit8" },
        { KeyboardKey.Digit9, "Digit9" },
        { KeyboardKey.ArrowUp, "ArrowUp" },
        { KeyboardKey.ArrowDown, "ArrowDown" },
        { KeyboardKey.ArrowLeft, "ArrowLeft" },
        { KeyboardKey.ArrowRight, "ArrowRight" },
        { KeyboardKey.Enter, "Enter" },
        { KeyboardKey.Escape, "Escape" },
        { KeyboardKey.Space, "Space" },
        { KeyboardKey.Tab, "Tab" },
        { KeyboardKey.Backspace, "Backspace" }
    };

    public static TheoryData<string, object> InvalidHotkeyTokens =>
        new()
        {
            { "\"a\"", SettingsJsonErrorCode.UnknownHotkeyKey },
            { "\"f8\"", SettingsJsonErrorCode.UnknownHotkeyKey },
            { "\"F12\"", SettingsJsonErrorCode.UnknownHotkeyKey },
            { "\"Digit0\"", SettingsJsonErrorCode.UnknownHotkeyKey },
            { "8", SettingsJsonErrorCode.ExpectedString },
            { "\"8\"", SettingsJsonErrorCode.UnknownHotkeyKey },
            { "\"VK_F8\"", SettingsJsonErrorCode.UnknownHotkeyKey },
            { "\"0x77\"", SettingsJsonErrorCode.UnknownHotkeyKey }
        };

    public static TheoryData<string, object> InvalidOutputTokens =>
        new()
        {
            { "\"m\"", SettingsJsonErrorCode.UnknownOutputKey },
            { "\"F1\"", SettingsJsonErrorCode.UnknownOutputKey },
            { "\"0\"", SettingsJsonErrorCode.UnknownOutputKey },
            { "12", SettingsJsonErrorCode.ExpectedString },
            { "\"0x4D\"", SettingsJsonErrorCode.UnknownOutputKey },
            { "\"Return\"", SettingsJsonErrorCode.UnknownOutputKey }
        };

    public static TheoryData<string, HotkeyModifiers> AcceptedModifierArrays =>
        new()
        {
            { "[]", HotkeyModifiers.None },
            { "[\"Control\"]", HotkeyModifiers.Control },
            {
                "[\"Shift\",\"Control\",\"Alt\"]",
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift
            }
        };

    public static TheoryData<string, object, string>
        InvalidModifierArrays => new()
        {
            {
                "[\"Control\",\"Control\"]",
                SettingsJsonErrorCode.DuplicateModifier,
                "macro.hotkey.modifiers[1]"
            },
            {
                "[\"Meta\"]",
                SettingsJsonErrorCode.UnknownModifier,
                "macro.hotkey.modifiers[0]"
            },
            {
                "[\"None\"]",
                SettingsJsonErrorCode.UnknownModifier,
                "macro.hotkey.modifiers[0]"
            },
            {
                "[\"control\"]",
                SettingsJsonErrorCode.UnknownModifier,
                "macro.hotkey.modifiers[0]"
            },
            {
                "[1]",
                SettingsJsonErrorCode.ExpectedString,
                "macro.hotkey.modifiers[0]"
            }
        };

    public static TheoryData<HotkeyModifiers, string> WriterModifierCases => new()
    {
        { HotkeyModifiers.None, string.Empty },
        { HotkeyModifiers.Control, "Control" },
        { HotkeyModifiers.Alt, "Alt" },
        { HotkeyModifiers.Shift, "Shift" },
        { HotkeyModifiers.Control | HotkeyModifiers.Alt, "Control,Alt" },
        { HotkeyModifiers.Control | HotkeyModifiers.Shift, "Control,Shift" },
        { HotkeyModifiers.Alt | HotkeyModifiers.Shift, "Alt,Shift" },
        {
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            "Control,Alt,Shift"
        }
    };

    [Theory]
    [MemberData(nameof(HotkeyTokens))]
    public void HotkeyToken_ReadsAndWritesCanonicalValue(
        HotkeyKey expectedKey,
        string token)
    {
        string modifiers = expectedKey >= HotkeyKey.A && expectedKey <= HotkeyKey.Z
            ? "[\"Control\"]"
            : "[]";
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                hotkeyJson: SettingsJsonTestSupport.HotkeyJson(
                    $"\"{token}\"",
                    modifiers)));

        ApplicationSettings settings = SettingsJsonTestSupport.AssertSuccess(
            SettingsJsonCodec.Read(json));
        Assert.Equal(expectedKey, settings.Macro.Hotkey.Key);

        using JsonDocument written = JsonDocument.Parse(
            SettingsJsonCodec.Write(settings));
        Assert.Equal(
            token,
            written.RootElement
                .GetProperty("macro")
                .GetProperty("hotkey")
                .GetProperty("key")
                .GetString());
    }

    [Theory]
    [MemberData(nameof(OutputKeyTokens))]
    public void OutputKeyToken_ReadsAndWritesCanonicalValue(
        KeyboardKey expectedKey,
        string token)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                stepsJson: SettingsJsonTestSupport.KeyTapStepsJson(
                    $"\"{token}\"")));

        ApplicationSettings settings = SettingsJsonTestSupport.AssertSuccess(
            SettingsJsonCodec.Read(json));
        KeyTapStep step = Assert.IsType<KeyTapStep>(
            Assert.Single(settings.Macro.Sequence.Steps));
        Assert.Equal(expectedKey, step.Key);

        using JsonDocument written = JsonDocument.Parse(
            SettingsJsonCodec.Write(settings));
        Assert.Equal(
            token,
            written.RootElement
                .GetProperty("macro")
                .GetProperty("steps")[0]
                .GetProperty("key")
                .GetString());
    }

    [Theory]
    [MemberData(nameof(InvalidHotkeyTokens))]
    public void HotkeyToken_RejectsNonCanonicalForms(
        string keyJson,
        object expectedCode)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                hotkeyJson: SettingsJsonTestSupport.HotkeyJson(keyJson)));

        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            "macro.hotkey.key");
    }

    [Theory]
    [MemberData(nameof(InvalidOutputTokens))]
    public void OutputToken_RejectsNonCanonicalForms(
        string keyJson,
        object expectedCode)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                stepsJson: SettingsJsonTestSupport.KeyTapStepsJson(keyJson)));

        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            "macro.steps[0].key");
    }

    [Theory]
    [MemberData(nameof(AcceptedModifierArrays))]
    public void ModifiersReader_AcceptsCanonicalTokensInAnyOrder(
        string modifiersJson,
        HotkeyModifiers expected)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                hotkeyJson: SettingsJsonTestSupport.HotkeyJson(
                    modifiersJson: modifiersJson)));

        ApplicationSettings settings = SettingsJsonTestSupport.AssertSuccess(
            SettingsJsonCodec.Read(json));
        Assert.Equal(expected, settings.Macro.Hotkey.Modifiers);
    }

    [Theory]
    [MemberData(nameof(InvalidModifierArrays))]
    public void ModifiersReader_RejectsDuplicateUnknownAndNonStringTokens(
        string modifiersJson,
        object expectedCode,
        string expectedPath)
    {
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(
                hotkeyJson: SettingsJsonTestSupport.HotkeyJson(
                    modifiersJson: modifiersJson)));

        SettingsJsonTestSupport.AssertCodecFailure(
            json,
            Assert.IsType<SettingsJsonErrorCode>(expectedCode),
            expectedPath);
    }

    [Theory]
    [MemberData(nameof(WriterModifierCases))]
    public void Writer_EmitsOnlyPresentModifiersInControlAltShiftOrder(
        HotkeyModifiers modifiers,
        string expectedCsv)
    {
        ApplicationSettings settings = SettingsJsonTestSupport.CreateSettings(
            hotkey: new KeyboardHotkey(HotkeyKey.F8, modifiers));

        using JsonDocument document = JsonDocument.Parse(
            SettingsJsonCodec.Write(settings));
        string actualCsv = string.Join(
            ",",
            document.RootElement
                .GetProperty("macro")
                .GetProperty("hotkey")
                .GetProperty("modifiers")
                .EnumerateArray()
                .Select(element => element.GetString()));

        Assert.Equal(expectedCsv, actualCsv);
    }
}
