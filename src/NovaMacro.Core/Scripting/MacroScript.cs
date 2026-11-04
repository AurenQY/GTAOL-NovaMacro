using System.Globalization;
using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Macros;

namespace NovaMacro.Core.Scripting;

public enum ScriptCommandKind
{
    Key,
    GameKey,
    Mouse,
    Move,
    Wait,
    Path,
    Position,
}

public enum KeyAction
{
    Tap,
    Down,
    Up,
}

public sealed record ScriptCommand(
    ScriptCommandKind Kind,
    int Line,
    int Code = 0,
    GameAction Action = default,
    KeyAction KeyAction = KeyAction.Tap,
    int Interval = 50,
    int Delay = 10,
    int Times = 1,
    int X = 0,
    int Y = 0,
    int Milliseconds = 0,
    string? Text = null)
{
    public string Describe() => Kind switch
    {
        ScriptCommandKind.Key => $"{Verb}{KeyNames.GetName(Code)}({Interval}间隔,{Delay}延迟){TimesText}",
        ScriptCommandKind.Mouse => $"{Verb}{KeyNames.GetName(Code)}({Interval}间隔,{Delay}延迟){TimesText}",
        ScriptCommandKind.GameKey => $"{Verb}{GameActions.Info(Action).DisplayName}({Interval}间隔,{Delay}延迟){TimesText}",
        ScriptCommandKind.Move => $"游戏坐标{X},{Y}",
        ScriptCommandKind.Wait => $"等待{Milliseconds}毫秒",
        ScriptCommandKind.Path => $"路径 {Text}",
        ScriptCommandKind.Position => "呼出并定位至【生命值与弹药】",
        _ => Kind.ToString(),
    };

    private string Verb => KeyAction switch
    {
        KeyAction.Down => "按下",
        KeyAction.Up => "弹起",
        _ => "点击",
    };

    private string TimesText => Times > 1 ? $" ×{Times}" : string.Empty;
}

public sealed class ScriptParseException(int line, string message) : Exception($"自定义宏 *发现错误代码：第{line}行 {message}")
{
    public int Line { get; } = line;
}

/// <summary>
/// Custom macro script. One command per line:
/// <code>
/// 键 A键 [点击|按下|弹起] [间隔=50] [延迟=10] [次数=1]
/// 游戏 武器射击 [点击|按下|弹起] …        (uses the GTA binding of a game action)
/// 鼠标 左键 [点击|按下|弹起] …
/// 移动 960,540                              (game-window client coordinates)
/// 等待 500  /  等待 2秒
/// 路径 M,↓4,确认                            (menu path, see MenuPath)
/// 定位 生命值与弹药                          (open M menu at 生命值与弹药)
/// // 注释
/// </code>
/// English keywords key/game/mouse/move/wait/path/position are accepted as well.
/// </summary>
public static class MacroScript
{
    public static IReadOnlyList<ScriptCommand> Parse(string script)
    {
        var commands = new List<ScriptCommand>();
        var lines = (script ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var line = lines[i].Trim();
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment].Trim();
            }

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts[0].ToLowerInvariant();
            var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            commands.Add(verb switch
            {
                "键" or "按键" or "key" => ParseKey(ScriptCommandKind.Key, rest, lineNo),
                "鼠标" or "mouse" => ParseKey(ScriptCommandKind.Mouse, rest, lineNo),
                "游戏" or "游戏键" or "game" => ParseKey(ScriptCommandKind.GameKey, rest, lineNo),
                "移动" or "坐标" or "游戏坐标" or "move" => ParseMove(rest, lineNo),
                "等待" or "延时" or "wait" or "sleep" => ParseWait(rest, lineNo),
                "路径" or "path" => ParsePath(rest, lineNo),
                "定位" or "position" => new ScriptCommand(ScriptCommandKind.Position, lineNo, Text: rest),
                _ => throw new ScriptParseException(lineNo, $"未知命令“{parts[0]}”"),
            });
        }

        return commands;
    }

    private static ScriptCommand ParseKey(ScriptCommandKind kind, string rest, int line)
    {
        var tokens = rest.Split(new[] { ' ', '\t', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new ScriptParseException(line, "缺少按键");
        }

        var code = 0;
        GameAction action = default;
        if (kind == ScriptCommandKind.GameKey)
        {
            if (!GameActions.TryParse(tokens[0], out action))
            {
                throw new ScriptParseException(line, $"未知游戏动作“{tokens[0]}”");
            }
        }
        else if (!KeyNames.TryParse(tokens[0], out code))
        {
            throw new ScriptParseException(line, $"未知按键“{tokens[0]}”");
        }

        if (kind == ScriptCommandKind.Mouse && !InputCodes.IsMouseButton(code) && !InputCodes.IsWheel(code))
        {
            throw new ScriptParseException(line, $"“{tokens[0]}”不是鼠标按键");
        }

        var keyAction = KeyAction.Tap;
        int interval = 50, delay = 10, times = 1;
        var positional = 0;
        foreach (var token in tokens.Skip(1))
        {
            switch (token)
            {
                case "点击" or "tap" or "click":
                    keyAction = KeyAction.Tap;
                    continue;
                case "按下" or "按住" or "down":
                    keyAction = KeyAction.Down;
                    continue;
                case "弹起" or "松开" or "up":
                    keyAction = KeyAction.Up;
                    continue;
            }

            var eq = token.IndexOfAny(new[] { '=', '：', ':' });
            if (eq > 0)
            {
                var name = token[..eq];
                var value = ParseInt(token[(eq + 1)..], line);
                switch (name)
                {
                    case "间隔" or "interval" or "hold":
                        interval = value;
                        break;
                    case "延迟" or "delay" or "after":
                        delay = value;
                        break;
                    case "次数" or "times":
                        times = Math.Max(1, value);
                        break;
                    default:
                        throw new ScriptParseException(line, $"未知参数“{name}”");
                }

                continue;
            }

            var number = ParseInt(token, line);
            switch (positional++)
            {
                case 0:
                    interval = number;
                    break;
                case 1:
                    delay = number;
                    break;
                case 2:
                    times = Math.Max(1, number);
                    break;
                default:
                    throw new ScriptParseException(line, "参数过多");
            }
        }

        return new ScriptCommand(kind, line, code, action, keyAction, interval, delay, times);
    }

    private static ScriptCommand ParseMove(string rest, int line)
    {
        var tokens = rest.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2)
        {
            throw new ScriptParseException(line, "坐标格式应为 x,y");
        }

        return new ScriptCommand(ScriptCommandKind.Move, line, X: ParseInt(tokens[0], line), Y: ParseInt(tokens[1], line));
    }

    private static ScriptCommand ParseWait(string rest, int line)
    {
        var text = rest.Trim();
        var multiplier = 1;
        if (text.EndsWith("毫秒", StringComparison.Ordinal) || text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            text = text.EndsWith("毫秒", StringComparison.Ordinal) ? text[..^2] : text[..^2];
        }
        else if (text.EndsWith("秒", StringComparison.Ordinal) || text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1];
            multiplier = 1000;
        }

        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new ScriptParseException(line, $"无效的等待时间“{rest}”");
        }

        return new ScriptCommand(ScriptCommandKind.Wait, line, Milliseconds: (int)Math.Round(value * multiplier));
    }

    private static ScriptCommand ParsePath(string rest, int line)
    {
        if (MenuPath.Parse(rest, _ => 0).Count == 0)
        {
            throw new ScriptParseException(line, "路径为空或无法识别");
        }

        return new ScriptCommand(ScriptCommandKind.Path, line, Text: rest);
    }

    private static int ParseInt(string text, int line)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new ScriptParseException(line, $"无效的数字“{text}”");
        }

        return value;
    }
}
