using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration.Json;

internal static class HotkeyKeyTokenMapper
{
    private static readonly IReadOnlyDictionary<string, HotkeyKey> FromToken =
        new Dictionary<string, HotkeyKey>(StringComparer.Ordinal)
        {
            ["A"] = HotkeyKey.A,
            ["B"] = HotkeyKey.B,
            ["C"] = HotkeyKey.C,
            ["D"] = HotkeyKey.D,
            ["E"] = HotkeyKey.E,
            ["F"] = HotkeyKey.F,
            ["G"] = HotkeyKey.G,
            ["H"] = HotkeyKey.H,
            ["I"] = HotkeyKey.I,
            ["J"] = HotkeyKey.J,
            ["K"] = HotkeyKey.K,
            ["L"] = HotkeyKey.L,
            ["M"] = HotkeyKey.M,
            ["N"] = HotkeyKey.N,
            ["O"] = HotkeyKey.O,
            ["P"] = HotkeyKey.P,
            ["Q"] = HotkeyKey.Q,
            ["R"] = HotkeyKey.R,
            ["S"] = HotkeyKey.S,
            ["T"] = HotkeyKey.T,
            ["U"] = HotkeyKey.U,
            ["V"] = HotkeyKey.V,
            ["W"] = HotkeyKey.W,
            ["X"] = HotkeyKey.X,
            ["Y"] = HotkeyKey.Y,
            ["Z"] = HotkeyKey.Z,
            ["F1"] = HotkeyKey.F1,
            ["F2"] = HotkeyKey.F2,
            ["F3"] = HotkeyKey.F3,
            ["F4"] = HotkeyKey.F4,
            ["F5"] = HotkeyKey.F5,
            ["F6"] = HotkeyKey.F6,
            ["F7"] = HotkeyKey.F7,
            ["F8"] = HotkeyKey.F8,
            ["F9"] = HotkeyKey.F9,
            ["F10"] = HotkeyKey.F10,
            ["F11"] = HotkeyKey.F11,
            ["Numpad0"] = HotkeyKey.Numpad0,
            ["Numpad1"] = HotkeyKey.Numpad1,
            ["Numpad2"] = HotkeyKey.Numpad2,
            ["Numpad3"] = HotkeyKey.Numpad3,
            ["Numpad4"] = HotkeyKey.Numpad4,
            ["Numpad5"] = HotkeyKey.Numpad5,
            ["Numpad6"] = HotkeyKey.Numpad6,
            ["Numpad7"] = HotkeyKey.Numpad7,
            ["Numpad8"] = HotkeyKey.Numpad8,
            ["Numpad9"] = HotkeyKey.Numpad9,
            ["NumpadAdd"] = HotkeyKey.NumpadAdd,
            ["NumpadSubtract"] = HotkeyKey.NumpadSubtract,
            ["NumpadMultiply"] = HotkeyKey.NumpadMultiply,
            ["NumpadDivide"] = HotkeyKey.NumpadDivide,
            ["NumpadDecimal"] = HotkeyKey.NumpadDecimal
        };

    private static readonly IReadOnlyDictionary<HotkeyKey, string> ToToken =
        FromToken.ToDictionary(pair => pair.Value, pair => pair.Key);

    internal static bool TryParse(string token, out HotkeyKey key)
    {
        return FromToken.TryGetValue(token, out key);
    }

    internal static bool TryFormat(HotkeyKey key, out string? token)
    {
        return ToToken.TryGetValue(key, out token);
    }
}

internal static class KeyboardKeyTokenMapper
{
    private static readonly IReadOnlyDictionary<string, KeyboardKey> FromToken =
        new Dictionary<string, KeyboardKey>(StringComparer.Ordinal)
        {
            ["A"] = KeyboardKey.A,
            ["B"] = KeyboardKey.B,
            ["C"] = KeyboardKey.C,
            ["D"] = KeyboardKey.D,
            ["E"] = KeyboardKey.E,
            ["F"] = KeyboardKey.F,
            ["G"] = KeyboardKey.G,
            ["H"] = KeyboardKey.H,
            ["I"] = KeyboardKey.I,
            ["J"] = KeyboardKey.J,
            ["K"] = KeyboardKey.K,
            ["L"] = KeyboardKey.L,
            ["M"] = KeyboardKey.M,
            ["N"] = KeyboardKey.N,
            ["O"] = KeyboardKey.O,
            ["P"] = KeyboardKey.P,
            ["Q"] = KeyboardKey.Q,
            ["R"] = KeyboardKey.R,
            ["S"] = KeyboardKey.S,
            ["T"] = KeyboardKey.T,
            ["U"] = KeyboardKey.U,
            ["V"] = KeyboardKey.V,
            ["W"] = KeyboardKey.W,
            ["X"] = KeyboardKey.X,
            ["Y"] = KeyboardKey.Y,
            ["Z"] = KeyboardKey.Z,
            ["Digit0"] = KeyboardKey.Digit0,
            ["Digit1"] = KeyboardKey.Digit1,
            ["Digit2"] = KeyboardKey.Digit2,
            ["Digit3"] = KeyboardKey.Digit3,
            ["Digit4"] = KeyboardKey.Digit4,
            ["Digit5"] = KeyboardKey.Digit5,
            ["Digit6"] = KeyboardKey.Digit6,
            ["Digit7"] = KeyboardKey.Digit7,
            ["Digit8"] = KeyboardKey.Digit8,
            ["Digit9"] = KeyboardKey.Digit9,
            ["ArrowUp"] = KeyboardKey.ArrowUp,
            ["ArrowDown"] = KeyboardKey.ArrowDown,
            ["ArrowLeft"] = KeyboardKey.ArrowLeft,
            ["ArrowRight"] = KeyboardKey.ArrowRight,
            ["Enter"] = KeyboardKey.Enter,
            ["Escape"] = KeyboardKey.Escape,
            ["Space"] = KeyboardKey.Space,
            ["Tab"] = KeyboardKey.Tab,
            ["Backspace"] = KeyboardKey.Backspace,
            ["Numpad0"] = KeyboardKey.Numpad0,
            ["Numpad1"] = KeyboardKey.Numpad1,
            ["Numpad2"] = KeyboardKey.Numpad2,
            ["Numpad3"] = KeyboardKey.Numpad3,
            ["Numpad4"] = KeyboardKey.Numpad4,
            ["Numpad5"] = KeyboardKey.Numpad5,
            ["Numpad6"] = KeyboardKey.Numpad6,
            ["Numpad7"] = KeyboardKey.Numpad7,
            ["Numpad8"] = KeyboardKey.Numpad8,
            ["Numpad9"] = KeyboardKey.Numpad9,
            ["NumpadAdd"] = KeyboardKey.NumpadAdd,
            ["NumpadSubtract"] = KeyboardKey.NumpadSubtract,
            ["NumpadMultiply"] = KeyboardKey.NumpadMultiply,
            ["NumpadDivide"] = KeyboardKey.NumpadDivide,
            ["NumpadDecimal"] = KeyboardKey.NumpadDecimal,
            ["NumpadEnter"] = KeyboardKey.NumpadEnter
        };

    private static readonly IReadOnlyDictionary<KeyboardKey, string> ToToken =
        FromToken.ToDictionary(pair => pair.Value, pair => pair.Key);

    internal static bool TryParse(string token, out KeyboardKey key)
    {
        return FromToken.TryGetValue(token, out key);
    }

    internal static bool TryFormat(KeyboardKey key, out string? token)
    {
        return ToToken.TryGetValue(key, out token);
    }
}
