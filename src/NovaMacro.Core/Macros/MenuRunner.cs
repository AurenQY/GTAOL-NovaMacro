using NovaMacro.Core.Game;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Macros;

/// <summary>Executes <see cref="MenuPath"/> steps with M-menu timing from settings.</summary>
public static class MenuRunner
{
    public static KeyTiming MenuTiming(SettingsStore settings)
    {
        var floor = settings.GetInt(Sec.MenuLayout, "最低按键延迟");
        return new KeyTiming(
            settings.GetInt(Sec.MenuSnack, "按键按下弹起间隔"),
            Math.Max(settings.GetInt(Sec.MenuSnack, "按键延迟"), floor));
    }

    public static int? Placeholder(SettingsStore settings, string name)
    {
        var raw = settings.GetString(Sec.MenuLayout, name);
        return int.TryParse(raw, out var v) ? v : null;
    }

    public static IReadOnlyList<MenuStep> Load(SettingsStore settings, string pathKey, Func<string, int?>? extra = null)
    {
        var text = settings.GetString(Sec.MenuLayout, pathKey);
        return MenuPath.Parse(text, name => extra?.Invoke(name) ?? Placeholder(settings, name));
    }

    public static void Run(MacroContext ctx, IReadOnlyList<MenuStep> steps, Action<string>? onMark = null, KeyTiming? timing = null)
    {
        var t = timing ?? MenuTiming(ctx.Settings);
        var openDelay = ctx.Settings.GetInt(Sec.MenuLayout, "菜单打开延迟");
        var subDelay = ctx.Settings.GetInt(Sec.MenuLayout, "子菜单切换延迟");
        var useWheel = ctx.Settings.GetString(Sec.MenuSnack, "M菜单选择模式") == "鼠标滚轮";
        foreach (var step in steps)
        {
            ctx.Token.ThrowIfCancellationRequested();
            switch (step.Kind)
            {
                case MenuStepKind.Wait:
                    ctx.Wait(step.Milliseconds);
                    break;
                case MenuStepKind.Mark:
                    onMark?.Invoke(step.Mark ?? string.Empty);
                    break;
                case MenuStepKind.Key:
                    for (var i = 0; i < step.Count; i++)
                    {
                        ctx.Tap(step.Code, t);
                    }

                    break;
                case MenuStepKind.Action:
                    var action = step.Action;
                    if (useWheel && action == GameAction.MenuDown)
                    {
                        action = GameAction.PhoneScrollForward;
                    }
                    else if (useWheel && action == GameAction.MenuUp)
                    {
                        action = GameAction.PhoneScrollBackward;
                    }

                    ctx.Press(action, t, step.Count);
                    if (action is GameAction.InteractionMenu or GameAction.Pause or GameAction.Phone or GameAction.SocialClubHome)
                    {
                        ctx.Wait(openDelay);
                    }
                    else if (action is GameAction.MenuSelect or GameAction.MenuBack)
                    {
                        ctx.Wait(subDelay);
                    }

                    break;
            }
        }
    }

    public static void Run(MacroContext ctx, string pathKey, Func<string, int?>? extra = null, Action<string>? onMark = null) =>
        Run(ctx, Load(ctx.Settings, pathKey, extra), onMark);
}
