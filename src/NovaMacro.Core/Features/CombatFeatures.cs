using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Features;

/// <summary>
/// 武器卡弹 — fire then switch weapon away and back to skip reload/cool-down
/// (rapid fire for heavy weapons / sniper rifles).
/// </summary>
public sealed class WeaponJamFeature : MacroFeature
{
    private volatile bool _fireActivated;

    public WeaponJamFeature()
        : base(FeatureId.WeaponJam)
    {
    }

    public static GameAction SlotFor(string weapon) => weapon switch
    {
        "狙击步枪" => GameAction.WeaponSniper,
        "突击步枪" => GameAction.WeaponAssaultRifle,
        "手枪" => GameAction.WeaponPistol,
        "机枪" => GameAction.WeaponSmg,
        "霰弹枪" => GameAction.WeaponShotgun,
        _ => GameAction.WeaponHeavy,
    };

    public string TargetWeapon => S.Settings.GetString(Sec.WeaponJam, "欲切枪武器键");

    public string ModeFor(string weapon)
    {
        if (S.Settings.GetBool(Sec.WeaponJam, "宏模式独立设置"))
        {
            var own = S.Settings.GetString(Sec.WeaponJam, "宏模式_" + weapon);
            if (own != "默认模式" || S.Settings.GetString(Sec.WeaponJam, "宏模式") == "默认模式")
            {
                return own;
            }
        }

        return S.Settings.GetString(Sec.WeaponJam, "宏模式");
    }

    public int IntervalFor(string weapon)
    {
        var key = "自动速射间隔_" + (weapon == "狙击步枪" ? "狙击枪" : weapon);
        var own = S.Settings.GetInt(Sec.WeaponJam, key);
        return own > 0 ? own : S.Settings.GetInt(Sec.WeaponJam, "懒人速射间隔");
    }

    public void RotateWeapon()
    {
        var enabled = SettingsSchema.WeaponNames.Where(w => S.Settings.GetBool(Sec.WeaponJam, "启用" + w)).ToList();
        if (enabled.Count == 0)
        {
            enabled = SettingsSchema.WeaponNames.ToList();
        }

        var next = enabled[(enabled.IndexOf(TargetWeapon) + 1) % enabled.Count];
        S.Settings.Set(Sec.WeaponJam, "欲切枪武器键", next);
        if (!S.Settings.GetBool(Sec.WeaponJam, "隐藏切枪提示"))
        {
            S.Notifier.Tip($"[武器卡弹] 当前欲切枪武器：{next} | 宏模式：{ModeFor(next)}");
        }
    }

    public void RotateMode()
    {
        if (S.Settings.GetBool(Sec.WeaponJam, "锁定宏模式"))
        {
            S.Notifier.Tip("[武器卡弹] 宏模式已锁定");
            return;
        }

        var modes = new[] { "默认模式", "自动速射", "单次切枪", "开火激活" };
        var current = S.Settings.GetString(Sec.WeaponJam, "宏模式");
        var next = modes[(Array.IndexOf(modes, current) + 1) % modes.Length];
        S.Settings.Set(Sec.WeaponJam, "宏模式", next);
        S.Notifier.Tip("[武器卡弹] 宏模式：" + next);
    }

    public override void OnHotkeyDown()
    {
        var rotateModifier = (ModifierMask)S.Settings.GetInt(Sec.WeaponJam, "切换欲切枪武器热键功能键");
        if (rotateModifier != ModifierMask.None && S.InputState.IsModifierDown(rotateModifier))
        {
            RotateWeapon();
            return;
        }

        if (S.IsAuxDown)
        {
            RotateMode();
            return;
        }

        var weapon = TargetWeapon;
        var mode = ModeFor(weapon);
        switch (mode)
        {
            case "开火激活":
                _fireActivated = !_fireActivated;
                S.Notifier.Tip(_fireActivated ? "[武器卡弹] 开火激活：已开启" : "[武器卡弹] 开火激活：已关闭");
                break;
            case "单次切枪":
                Start(ctx => SwitchWeapon(ctx, weapon));
                break;
            case "自动速射":
                Start(ctx => RapidFire(ctx, weapon));
                break;
            default:
                Start(ctx =>
                {
                    var t = Timing(ctx.Settings);
                    ctx.Press(GameAction.Attack, t);
                    SwitchWeapon(ctx, weapon);
                });
                break;
        }
    }

    public override bool OnInput(InputEventArgs e)
    {
        if (_fireActivated && !e.IsDown && e.Code == S.Input.Code(GameAction.Attack))
        {
            var weapon = TargetWeapon;
            Start(ctx => SwitchWeapon(ctx, weapon));
        }

        return false;
    }

    public override void OnEnabledChanged(bool enabled)
    {
        base.OnEnabledChanged(enabled);
        if (!enabled)
        {
            _fireActivated = false;
        }
    }

    private static KeyTiming Timing(SettingsStore settings) =>
        new(settings.GetInt(Sec.WeaponJam, "按键间隔"), settings.GetInt(Sec.WeaponJam, "按键延迟"));

    private void RapidFire(MacroContext ctx, string weapon)
    {
        var t = Timing(ctx.Settings);
        var interval = IntervalFor(weapon);
        var fireFirst = ctx.Settings.GetString(Sec.WeaponJam, "自动速射开火顺序") != "先切枪后开火";
        var endMode = ctx.Settings.GetString(Sec.WeaponJam, "宏结束操作");
        var throwKey = ctx.Settings.GetInt(Sec.WeaponJam, "投掷武器键位");
        ctx.Log($"投掷武器键：{KeyNames.GetName(throwKey == InputCodes.None ? ctx.Input.Code(GameAction.WeaponThrowable) : throwKey)} | 自动速射间隔：{interval} | 宏模式：自动速射 | 欲切枪武器键：{weapon}");
        var first = true;
        while (first || HotkeyHeld)
        {
            var abortable = endMode == "全部提前结束" || (endMode == "仅第一发完整" && !first);
            if (fireFirst)
            {
                ctx.Press(GameAction.Attack, t);
                if (abortable && !HotkeyHeld)
                {
                    break;
                }

                SwitchWeapon(ctx, weapon);
            }
            else
            {
                SwitchWeapon(ctx, weapon);
                if (abortable && !HotkeyHeld)
                {
                    break;
                }

                ctx.Press(GameAction.Attack, t);
            }

            first = false;
            WaitWhileHeld(ctx, interval, abortable || endMode != "全部操作完整");
        }
    }

    private void WaitWhileHeld(MacroContext ctx, int interval, bool abortOnRelease)
    {
        var remaining = interval;
        while (remaining > 0)
        {
            var slice = Math.Min(remaining, 10);
            ctx.Wait(-slice);
            remaining -= slice;
            if (abortOnRelease && !HotkeyHeld)
            {
                return;
            }
        }
    }

    private void SwitchWeapon(MacroContext ctx, string weapon)
    {
        var t = Timing(ctx.Settings);
        if (ctx.Settings.GetBool(Sec.WeaponJam, "武器轮盘式切换") && !ctx.Settings.GetBool(Sec.WeaponJam, "武器键位切换"))
        {
            ctx.Input.Output.Wheel(-1);
            ctx.Wait(t.After);
            ctx.Input.Output.Wheel(1);
            ctx.Wait(t.After);
            return;
        }

        var throwKey = ctx.Settings.GetInt(Sec.WeaponJam, "投掷武器键位");
        ctx.Tap(throwKey == InputCodes.None ? ctx.Input.Code(GameAction.WeaponThrowable) : throwKey, t);
        ctx.Press(SlotFor(weapon), t);
    }
}

/// <summary>连续左键 — aux + double left click starts auto clicking.</summary>
public sealed class AutoClickFeature : MacroFeature
{
    private long _lastLeftDown = -1000;
    private volatile bool _stopRequested;

    public AutoClickFeature()
        : base(FeatureId.AutoClick)
    {
    }

    public override bool OnInput(InputEventArgs e)
    {
        var interval = S.Settings.GetInt(Sec.AutoClick, "间隔时间");
        if (IsRunning && e.IsDown && interval < 0 && (e.Code == InputCodes.LeftButton || e.Code == S.AuxKey))
        {
            _stopRequested = true;
            return e.Code == InputCodes.LeftButton;
        }

        if (e.Code != InputCodes.LeftButton || !e.IsDown || !S.IsAuxDown)
        {
            return false;
        }

        if (e.Timestamp - _lastLeftDown <= 300 && !IsRunning)
        {
            _stopRequested = false;
            var toggle = interval < 0;
            var ms = Math.Max(1, Math.Abs(interval));
            Start(ctx =>
            {
                ctx.Tip("[连续左键] 开启 | 按键间隔：" + ms);
                var t = new KeyTiming(Math.Min(10, ms), ms);
                while (!_stopRequested && (toggle || ctx.Services.IsAuxDown))
                {
                    ctx.Tap(InputCodes.LeftButton, t);
                }

                ctx.Tip("[连续左键] 关闭");
            });
            _lastLeftDown = -1000;
            return true;
        }

        _lastLeftDown = e.Timestamp;
        return false;
    }
}

/// <summary>快速零食 — while the weapon wheel is open, holding the snack key (or a side button) eats snacks repeatedly.</summary>
public sealed class WheelSnackFeature : MacroFeature
{
    public WheelSnackFeature()
        : base(FeatureId.WheelSnack)
    {
    }

    private int TriggerCode => S.Settings.GetString(Sec.Other, "快速零食侧键") switch
    {
        "侧键上" => InputCodes.XButton2,
        "侧键下" => InputCodes.XButton1,
        _ => S.Input.Code(GameAction.EatSnack),
    };

    public override bool OnInput(InputEventArgs e)
    {
        var trigger = TriggerCode;
        if (e.Code != trigger || !e.IsDown || e.IsRepeat)
        {
            return false;
        }

        if (!S.InputState.IsDown(S.Input.Code(GameAction.WeaponWheel)))
        {
            return false;
        }

        Start(ctx =>
        {
            var snack = ctx.Input.Code(GameAction.EatSnack);
            var armor = ctx.Settings.GetBool(Sec.Other, "快速零食穿护甲");
            var t = new KeyTiming(20, ctx.Settings.GetInt(Sec.MenuSnack, "吃零食间隔"));
            ctx.Log("轮盘零食");
            while (ctx.IsDown(trigger) && ctx.IsDown(GameAction.WeaponWheel))
            {
                if (trigger == snack)
                {
                    ctx.Input.UpCode(snack);
                }

                ctx.Tap(snack, t);
                if (armor)
                {
                    ctx.Press(GameAction.UseArmor, t);
                }
            }
        });
        return trigger != S.Input.Code(GameAction.EatSnack);
    }
}

/// <summary>切枪偷速 — loops weapon switching while the configured chord is held.</summary>
public sealed class WeaponSwapSpeedFeature : MacroFeature
{
    public WeaponSwapSpeedFeature()
        : base(FeatureId.WeaponSwapSpeed)
    {
    }

    public override Hotkey? CustomHotkey => Hotkey.None;

    public Hotkey Chord => new(
        S.Settings.GetInt(Sec.Other, "切枪偷速键"),
        (ModifierMask)S.Settings.GetInt(Sec.Other, "切枪偷速功能键"),
        S.Settings.GetInt(Sec.Other, "切枪偷速副键"));

    public override bool OnInput(InputEventArgs e)
    {
        var chord = Chord;
        if (chord.IsEmpty || e.Code != chord.Key || !e.IsDown || e.IsRepeat)
        {
            return false;
        }

        if ((chord.Modifiers != ModifierMask.None && !S.InputState.IsModifierDown(chord.Modifiers)) ||
            (chord.Secondary != InputCodes.None && !S.InputState.IsDown(chord.Secondary)))
        {
            return false;
        }

        Start(ctx =>
        {
            var aim = ctx.Settings.GetBool(Sec.Other, "切枪偷速瞄准操作");
            var stopOnRelease = ctx.Settings.GetBool(Sec.Other, "切枪偷速松开停止");
            var fire = ctx.Settings.GetBool(Sec.Other, "切枪偷速触发开枪");
            var interval = ctx.Settings.GetInt(Sec.Other, "切枪偷速间隔");
            var t = new KeyTiming(20, 15);
            ctx.Log("开始切枪偷速");
            var aimHeld = false;
            try
            {
                while (!stopOnRelease || ctx.IsDown(chord.Key))
                {
                    if (aim && !aimHeld)
                    {
                        ctx.Input.Down(GameAction.Aim);
                        aimHeld = true;
                    }

                    ctx.Press(GameAction.WeaponPistol, t);
                    if (fire)
                    {
                        ctx.Press(GameAction.Attack, t);
                    }

                    ctx.Wait(interval);
                    ctx.Press(GameAction.WeaponUnarmed, t);
                    ctx.Wait(interval);
                    if (!stopOnRelease && ctx.IsDown(chord.Key) == false && ctx.IsDown(InputCodes.LeftButton))
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (aimHeld)
                {
                    ctx.Input.Up(GameAction.Aim);
                }

                ctx.Log("瞄准续偷速结束");
            }
        });
        return true;
    }
}
