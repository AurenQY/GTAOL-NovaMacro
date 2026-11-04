using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Features;

/// <summary>AD偷速 — Shift + W + E alternates A/D strafing while sprinting.</summary>
public sealed class AdSpeedFeature : MacroFeature
{
    public AdSpeedFeature()
        : base(FeatureId.AdSpeed)
    {
    }

    public override bool OnInput(InputEventArgs e)
    {
        var trigger = InputCodes.Letter('E');
        if (e.Code != trigger)
        {
            return false;
        }

        if (!e.IsDown)
        {
            return IsRunning;
        }

        if (!S.InputState.IsModifierDown(ModifierMask.Shift) || !S.InputState.IsDown(S.Input.Code(GameAction.MoveForward)))
        {
            return false;
        }

        if (!e.IsRepeat)
        {
            Start(ctx =>
            {
                ctx.Log("开始AD偷速跑");
                var t = new KeyTiming(30, 5);
                while (ctx.IsDown(trigger) && ctx.IsDown(GameAction.MoveForward))
                {
                    ctx.Press(GameAction.MoveLeft, t);
                    ctx.Press(GameAction.MoveRight, t);
                }
            });
        }

        return true;
    }
}

/// <summary>奔跑切换 — toggles a held sprint key; a long press (&gt;1s) does not toggle.</summary>
public sealed class SprintToggleFeature : MacroFeature
{
    private long _downAt;
    private volatile bool _sprinting;

    public SprintToggleFeature()
        : base(FeatureId.SprintToggle)
    {
    }

    public bool IsSprinting => _sprinting;

    public override void OnHotkeyDown() => _downAt = Runtime.Now;

    public override void OnHotkeyUp()
    {
        if (Runtime.Now - _downAt > 1000)
        {
            return;
        }

        if (_sprinting)
        {
            _sprinting = false;
            Cancel();
            S.Notifier.Tip("[奔跑切换] 已切换为不按住奔跑");
            return;
        }

        _sprinting = true;
        Start(ctx =>
        {
            ctx.Tip("[奔跑切换] 已切换为按住奔跑");
            // Hold sprint only while GTA is in the foreground, otherwise a stuck Shift would leak into
            // other applications after Alt+Tab.
            var held = false;
            try
            {
                while (_sprinting)
                {
                    var foreground = ctx.Services.Game.IsForeground;
                    if (foreground != held)
                    {
                        if (foreground)
                        {
                            ctx.Input.Down(GameAction.Sprint);
                        }
                        else
                        {
                            ctx.Input.Up(GameAction.Sprint);
                        }

                        held = foreground;
                    }

                    ctx.Wait(-50);
                }
            }
            finally
            {
                if (held)
                {
                    ctx.Input.Up(GameAction.Sprint);
                }

                _sprinting = false;
            }
        });
    }

    public override void OnEnabledChanged(bool enabled)
    {
        _sprinting = false;
        base.OnEnabledChanged(enabled);
    }
}

/// <summary>Phone helpers shared by 电话呼叫 and 延迟攀爬.</summary>
public static class PhoneMacro
{
    public static KeyTiming Timing(SettingsStore s) => new(s.GetInt(Sec.Phone, "手机_按下弹起间隔"), s.GetInt(Sec.Phone, "手机_滚动延迟"));

    /// <summary>Opens the phone and moves the cursor to a home-screen app (3 columns).</summary>
    public static void OpenApp(MacroContext ctx, int gridIndex, bool select = true)
    {
        var s = ctx.Settings;
        var t = Timing(s);
        ctx.Press(GameAction.Phone, t);
        ctx.Wait(s.GetInt(Sec.Phone, "手机_呼出后延迟"));
        var row = gridIndex / 3;
        var col = gridIndex % 3;
        ctx.Press(GameAction.MenuRight, t, col);
        ctx.Press(GameAction.MenuDown, t, row);
        if (select)
        {
            ctx.Press(GameAction.MenuSelect, t);
            ctx.Wait(s.GetInt(Sec.Phone, "手机_选择后延迟"));
        }
    }
}

/// <summary>延迟攀爬 — optionally switches to unarmed, then opens the phone camera after a delay.</summary>
public sealed class DelayClimbFeature : MacroFeature
{
    public DelayClimbFeature()
        : base(FeatureId.DelayClimb)
    {
    }

    public override void OnHotkeyDown()
    {
        if (IsRunning)
        {
            Cancel();
            S.Notifier.Tip("[延迟攀爬] 自动相机已停止");
            return;
        }

        Start(ctx =>
        {
            var s = ctx.Settings;
            var t = PhoneMacro.Timing(s);
            if (s.GetBool(Sec.DelayClimb, "自动切空手"))
            {
                ctx.Press(GameAction.WeaponUnarmed, t);
            }

            if (!s.GetBool(Sec.DelayClimb, "自动相机"))
            {
                return;
            }

            var delay = s.GetInt(Sec.DelayClimb, "自动打开相机时间");
            var camera = s.GetInt(Sec.PhoneCall, "相机位置");
            var preselect = s.GetBool(Sec.DelayClimb, "二次预选相机");
            ctx.Tip($"[延迟攀爬] 正在触发自动相机，{delay} 毫秒后打开");
            if (preselect)
            {
                PhoneMacro.OpenApp(ctx, camera, select: false);
                ctx.Wait(delay);
                ctx.Press(GameAction.MenuSelect, t);
            }
            else
            {
                ctx.Wait(delay);
                PhoneMacro.OpenApp(ctx, camera);
            }
        });
    }
}

/// <summary>瞬间悬停 — aux + character-switch key triggers the hover glitch.</summary>
public sealed class InstantHoverFeature : MacroFeature
{
    public InstantHoverFeature()
        : base(FeatureId.InstantHover)
    {
    }

    public override bool OnInput(InputEventArgs e)
    {
        if (!e.IsDown || e.IsRepeat || !S.IsAuxDown)
        {
            return false;
        }

        var keys = new[] { GameAction.CharacterWheel, GameAction.SelectMichael, GameAction.SelectFranklin, GameAction.SelectTrevor };
        if (!keys.Any(k => S.Input.Code(k) == e.Code))
        {
            return false;
        }

        Start(ctx =>
        {
            var s = ctx.Settings;
            var cut = s.GetBool(Sec.Hover, "断网悬停");
            var hold = s.GetInt(Sec.Hover, "悬停持续时间");
            var t = new KeyTiming(20, 20);
            if (cut)
            {
                ctx.Services.Tools.SetTemporaryBlock("断网悬停", true);
            }

            try
            {
                ctx.Tip("[瞬间悬停] 悬停中...");
                ctx.Input.Down(GameAction.CharacterWheel);
                ctx.Wait(50);
                ctx.Press(GameAction.SelectFranklin, t);
                ctx.Wait(hold);
            }
            finally
            {
                ctx.Input.Up(GameAction.CharacterWheel);
                ctx.Tap(InputCodes.Backspace, new KeyTiming(20, 50));
                if (cut)
                {
                    ctx.Services.Tools.SetTemporaryBlock("断网悬停", false);
                }

                ctx.Log("悬停结束");
            }
        });
        return true;
    }
}

/// <summary>快速人称 — toggles between first person and the chosen third-person distance.</summary>
public sealed class QuickPerspectiveFeature : MacroFeature
{
    public QuickPerspectiveFeature()
        : base(FeatureId.QuickPerspective)
    {
    }

    /// <summary>Presses needed to cycle from one camera state to another (0 first, 1 near, 2 mid, 3 far).</summary>
    public static int PressesBetween(int from, int to) => ((to - from) % 4 + 4) % 4;

    public override void OnHotkeyDown()
    {
        if (S.IsAuxDown)
        {
            S.Settings.SetInt(Sec.QuickPerspective, "当前人称", 0);
            S.Notifier.Tip("[快速人称] 已重置到第一人称");
            return;
        }

        Start(ctx =>
        {
            var s = ctx.Settings;
            var current = s.GetInt(Sec.QuickPerspective, "当前人称");
            var third = s.GetString(Sec.QuickPerspective, "快速切换第三人称") switch
            {
                "第三人称中" => 2,
                "第三人称远" => 3,
                _ => 1,
            };
            var target = current == 0 ? third : 0;
            var t = new KeyTiming(s.GetInt(Sec.QuickPerspective, "按键延迟"), s.GetInt(Sec.QuickPerspective, "按键间隔"));
            ctx.Press(GameAction.NextCamera, t, PressesBetween(current, target));
            s.SetInt(Sec.QuickPerspective, "当前人称", target);
            ctx.Log($"读取的游戏键位：{KeyNames.GetName(ctx.Input.Code(GameAction.NextCamera))} | 当前人称：{(target == 0 ? "第一人称" : "第三人称")}");
        });
    }
}

/// <summary>电话呼叫 — hold the hotkey, press 1-9 to choose a contact, release to call (default first).</summary>
public sealed class PhoneCallFeature : MacroFeature
{
    private volatile int _choice;

    public PhoneCallFeature()
        : base(FeatureId.PhoneCall)
    {
    }

    public static IReadOnlyList<(string Name, int Position)> ParseContacts(string text)
    {
        var result = new List<(string, int)>();
        foreach (var part in text.Split(';', '；', '|'))
        {
            var pieces = part.Split(',', '，');
            if (pieces.Length >= 2 && int.TryParse(pieces[^1].Trim(), out var pos))
            {
                result.Add((string.Join(",", pieces[..^1]).Trim(), pos));
            }
        }

        return result;
    }

    public override void OnHotkeyDown() => _choice = 0;

    public override bool OnInput(InputEventArgs e)
    {
        if (!HotkeyHeld || !e.IsDown)
        {
            return false;
        }

        for (var d = 1; d <= 9; d++)
        {
            if (e.Code == InputCodes.Digit(d) || e.Code == InputCodes.Numpad0 + d)
            {
                _choice = d;
                return true;
            }
        }

        return false;
    }

    public override void OnHotkeyUp()
    {
        var contacts = ParseContacts(S.Settings.GetString(Sec.PhoneCall, "电话位列"));
        if (contacts.Count == 0)
        {
            S.Notifier.Tip("[电话呼叫] 没有设置联系人");
            return;
        }

        var index = Math.Clamp(_choice == 0 ? 0 : _choice - 1, 0, contacts.Count - 1);
        var (name, position) = contacts[index];
        Start(ctx =>
        {
            var s = ctx.Settings;
            var t = PhoneMacro.Timing(s);
            ctx.Log($"触发键：{(_choice == 0 ? "默认" : _choice.ToString())} | 电话位列：{name}({position})");
            PhoneMacro.OpenApp(ctx, s.GetInt(Sec.PhoneCall, "通讯录位置"));
            ctx.Wait(s.GetInt(Sec.PhoneCall, "选择等待时间"));
            if (position >= 0)
            {
                ctx.Press(GameAction.MenuDown, t, position);
            }
            else
            {
                ctx.Press(GameAction.MenuUp, t, -position);
            }

            ctx.Press(GameAction.MenuSelect, t);
        });
    }
}
