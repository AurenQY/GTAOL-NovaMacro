using System.Xml.Linq;
using NovaMacro.Core.Input;

namespace NovaMacro.Core.Game;

/// <summary>
/// Reads GTA V key bindings from "Documents\Rockstar Games\&lt;GTA V|GTA V Enhanced&gt;\Profiles\&lt;id&gt;\control\user.xml"
/// (rage__ControlInput__ControlSettings/Mappings/Item).
/// </summary>
public static class GtaControlFile
{
    private static readonly Dictionary<string, int> GtaKeyCodes = BuildKeyTable();

    public static IReadOnlyDictionary<GameAction, int> Parse(string xml, bool preferSecondary = false)
    {
        var result = new Dictionary<GameAction, int>();
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return result;
        }

        var mappings = doc.Root?.Element("Mappings");
        if (mappings is null)
        {
            return result;
        }

        var byInput = GameActions.All.GroupBy(a => a.GtaInput, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Action).ToArray(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<GameAction, int>();
        foreach (var item in mappings.Elements("Item"))
        {
            var input = item.Element("Input")?.Value.Trim();
            if (input is null || !byInput.TryGetValue(input, out var actions))
            {
                continue;
            }

            var source = item.Element("Source")?.Value ?? string.Empty;
            if (source.Contains("PAD", StringComparison.OrdinalIgnoreCase) || source.Contains("JOYSTICK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameter = item.Element("Parameters")?.Elements("Item").FirstOrDefault()?.Value.Trim();
            if (parameter is null || !TryMapGtaKey(parameter, out var code))
            {
                continue;
            }

            foreach (var action in actions)
            {
                var count = seen.GetValueOrDefault(action);
                seen[action] = count + 1;
                // First mapping is the primary binding, the second one the secondary binding.
                if (count == 0 || (preferSecondary && count == 1))
                {
                    result[action] = code;
                }
            }
        }

        return result;
    }

    public static bool TryMapGtaKey(string gtaKey, out int code)
    {
        if (GtaKeyCodes.TryGetValue(gtaKey, out code))
        {
            return true;
        }

        code = InputCodes.None;
        return false;
    }

    /// <summary>Finds the newest profile control file below the documents "Rockstar Games" folder.</summary>
    public static string? FindControlFile(string documentsFolder, GtaEdition edition)
    {
        var gameFolder = edition == GtaEdition.Enhanced ? "GTA V Enhanced" : "GTA V";
        var profiles = Path.Combine(documentsFolder, "Rockstar Games", gameFolder, "Profiles");
        if (!Directory.Exists(profiles))
        {
            return null;
        }

        return Directory.EnumerateDirectories(profiles)
            .Select(p => Path.Combine(p, "control", "user.xml"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static Dictionary<string, int> BuildKeyTable()
    {
        var t = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MOUSE_LEFT"] = InputCodes.LeftButton,
            ["MOUSE_RIGHT"] = InputCodes.RightButton,
            ["MOUSE_MIDDLE"] = InputCodes.MiddleButton,
            ["MOUSE_EXTRABTN1"] = InputCodes.XButton1,
            ["MOUSE_EXTRABTN2"] = InputCodes.XButton2,
            ["IOM_WHEEL_UP"] = InputCodes.WheelUp,
            ["IOM_WHEEL_DOWN"] = InputCodes.WheelDown,
            ["MOUSE_WHEEL_UP"] = InputCodes.WheelUp,
            ["MOUSE_WHEEL_DOWN"] = InputCodes.WheelDown,
            ["KEY_BACK"] = 8, ["KEY_TAB"] = 9, ["KEY_CLEAR"] = 12, ["KEY_RETURN"] = 13, ["KEY_NUMPADENTER"] = InputCodes.NumpadEnter,
            ["KEY_SHIFT"] = 16, ["KEY_CONTROL"] = 17, ["KEY_MENU"] = 18, ["KEY_PAUSE"] = 19, ["KEY_CAPITAL"] = 20,
            ["KEY_ESCAPE"] = 27, ["KEY_SPACE"] = 32, ["KEY_PAGEUP"] = 33, ["KEY_PRIOR"] = 33, ["KEY_PAGEDOWN"] = 34, ["KEY_NEXT"] = 34,
            ["KEY_END"] = 35, ["KEY_HOME"] = 36, ["KEY_LEFT"] = 37, ["KEY_UP"] = 38, ["KEY_RIGHT"] = 39, ["KEY_DOWN"] = 40,
            ["KEY_SNAPSHOT"] = 44, ["KEY_INSERT"] = 45, ["KEY_DELETE"] = 46,
            ["KEY_LWIN"] = 91, ["KEY_RWIN"] = 92, ["KEY_APPS"] = 93,
            ["KEY_MULTIPLY"] = 106, ["KEY_ADD"] = 107, ["KEY_SEPARATOR"] = 108, ["KEY_SUBTRACT"] = 109, ["KEY_DECIMAL"] = 110, ["KEY_DIVIDE"] = 111,
            ["KEY_NUMLOCK"] = 144, ["KEY_SCROLL"] = 145,
            ["KEY_LSHIFT"] = 160, ["KEY_RSHIFT"] = 161, ["KEY_LCONTROL"] = 162, ["KEY_RCONTROL"] = 163, ["KEY_LMENU"] = 164, ["KEY_RMENU"] = 165,
            ["KEY_SEMICOLON"] = 186, ["KEY_PLUS"] = 187, ["KEY_EQUALS"] = 187, ["KEY_COMMA"] = 188, ["KEY_MINUS"] = 189, ["KEY_PERIOD"] = 190,
            ["KEY_SLASH"] = 191, ["KEY_OEM_3"] = 192, ["KEY_GRAVE"] = 192, ["KEY_LBRACKET"] = 219, ["KEY_BACKSLASH"] = 220,
            ["KEY_RBRACKET"] = 221, ["KEY_APOSTROPHE"] = 222, ["KEY_OEM_8"] = 223, ["KEY_OEM_102"] = 226,
        };
        for (var d = 0; d <= 9; d++)
        {
            t[$"KEY_{d}"] = InputCodes.Digit(d);
            t[$"KEY_NUMPAD{d}"] = InputCodes.Numpad0 + d;
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            t[$"KEY_{c}"] = InputCodes.Letter(c);
        }

        for (var f = 1; f <= 24; f++)
        {
            t[$"KEY_F{f}"] = InputCodes.Function(f);
        }

        return t;
    }
}
