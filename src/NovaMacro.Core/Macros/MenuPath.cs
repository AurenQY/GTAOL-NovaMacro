using System.Globalization;
using System.Text.RegularExpressions;
using NovaMacro.Core.Game;
using NovaMacro.Core.Input;

namespace NovaMacro.Core.Macros;

public enum MenuStepKind
{
    Action,
    Key,
    Wait,
    Mark,
}

/// <param name="Mark">For <see cref="MenuStepKind.Mark"/>: a named point a feature can hook (e.g. "零食").</param>
public sealed record MenuStep(MenuStepKind Kind, GameAction Action = default, int Code = 0, int Count = 1, int Milliseconds = 0, string? Mark = null);

/// <summary>
/// Compact, user-editable navigation paths, e.g. "M,↓{生命值与弹药位置},确认,←,↓,确认,M".
/// Tokens: M / 暂停 / HOME / 手机 / ↑n ↓n ←n →n / 确认 / 返回 / 等待n / 滚上n 滚下n /
/// 键:&lt;键名&gt;[*n] / 动作:&lt;GameAction&gt;[*n] / #标记. Placeholders {名称} are resolved
/// from the supplied lookup (negative positions move upwards).
/// </summary>
public static partial class MenuPath
{
    public static IReadOnlyList<MenuStep> Parse(string path, Func<string, int?>? placeholders = null)
    {
        var steps = new List<MenuStep>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return steps;
        }

        var expanded = PlaceholderRegex().Replace(path, m =>
        {
            var value = placeholders?.Invoke(m.Groups[1].Value);
            return value?.ToString(CultureInfo.InvariantCulture) ?? "0";
        });

        foreach (var raw in expanded.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (token.StartsWith('#'))
            {
                steps.Add(new MenuStep(MenuStepKind.Mark, Mark: token[1..]));
                continue;
            }

            if (TryDirection(token, steps))
            {
                continue;
            }

            if (token.StartsWith("等待", StringComparison.Ordinal) || token.StartsWith("wait", StringComparison.OrdinalIgnoreCase))
            {
                var digits = new string(token.SkipWhile(c => !char.IsDigit(c) && c != '-').ToArray());
                if (int.TryParse(digits, out var ms))
                {
                    steps.Add(new MenuStep(MenuStepKind.Wait, Milliseconds: ms));
                }

                continue;
            }

            var (name, count) = SplitCount(token);
            if (name.StartsWith("键:", StringComparison.Ordinal) || name.StartsWith("key:", StringComparison.OrdinalIgnoreCase))
            {
                if (KeyNames.TryParse(name[(name.IndexOf(':') + 1)..], out var code))
                {
                    steps.Add(new MenuStep(MenuStepKind.Key, Code: code, Count: count));
                }

                continue;
            }

            if (name.StartsWith("动作:", StringComparison.Ordinal) || name.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            {
                if (GameActions.TryParse(name[(name.IndexOf(':') + 1)..], out var a))
                {
                    steps.Add(new MenuStep(MenuStepKind.Action, a, Count: count));
                }

                continue;
            }

            GameAction? action = name switch
            {
                "M" or "m" or "M菜单" => GameAction.InteractionMenu,
                "确认" or "选择" or "回车" or "Enter" => GameAction.MenuSelect,
                "返回" or "Back" => GameAction.MenuBack,
                "暂停" or "Esc" or "ESC" => GameAction.Pause,
                "HOME" or "Home" => GameAction.SocialClubHome,
                "手机" => GameAction.Phone,
                "滚上" => GameAction.PhoneScrollBackward,
                "滚下" => GameAction.PhoneScrollForward,
                _ => null,
            };

            if (action is not null)
            {
                steps.Add(new MenuStep(MenuStepKind.Action, action.Value, Count: count));
                continue;
            }

            if (GameActions.TryParse(name, out var named))
            {
                steps.Add(new MenuStep(MenuStepKind.Action, named, Count: count));
                continue;
            }

            if (KeyNames.TryParse(name, out var key))
            {
                steps.Add(new MenuStep(MenuStepKind.Key, Code: key, Count: count));
            }
        }

        return steps;
    }

    private static bool TryDirection(string token, List<MenuStep> steps)
    {
        var first = token[0];
        GameAction? positive = first switch
        {
            '↓' => GameAction.MenuDown,
            '↑' => GameAction.MenuUp,
            '←' => GameAction.MenuLeft,
            '→' => GameAction.MenuRight,
            _ => null,
        };

        if (positive is null && (token.StartsWith("滚上") || token.StartsWith("滚下")))
        {
            var action = token.StartsWith("滚上") ? GameAction.PhoneScrollBackward : GameAction.PhoneScrollForward;
            var rest = token[2..];
            var n = rest.Length == 0 ? 1 : int.TryParse(rest, out var v) ? v : 1;
            if (n != 0)
            {
                steps.Add(new MenuStep(MenuStepKind.Action, action, Count: Math.Abs(n)));
            }

            return true;
        }

        if (positive is null)
        {
            return false;
        }

        var numberText = token[1..].Trim();
        var count = numberText.Length == 0 ? 1 : int.TryParse(numberText, out var c) ? c : 1;
        if (count == 0)
        {
            return true;
        }

        var actionFinal = positive.Value;
        if (count < 0)
        {
            actionFinal = actionFinal switch
            {
                GameAction.MenuDown => GameAction.MenuUp,
                GameAction.MenuUp => GameAction.MenuDown,
                GameAction.MenuLeft => GameAction.MenuRight,
                _ => GameAction.MenuLeft,
            };
            count = -count;
        }

        steps.Add(new MenuStep(MenuStepKind.Action, actionFinal, Count: count));
        return true;
    }

    private static (string Name, int Count) SplitCount(string token)
    {
        var star = token.LastIndexOfAny(new[] { '*', '×' });
        if (star > 0 && int.TryParse(token[(star + 1)..], out var count) && count > 0)
        {
            return (token[..star].Trim(), count);
        }

        return (token, 1);
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderRegex();
}
