using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Macros;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Features;

/// <summary>切换战局 — hold Pause (or XB_Menu) and press 1/2/3 or ←/↓/→ to switch session type.</summary>
public sealed class SwitchSessionFeature : MacroFeature
{
    public SwitchSessionFeature()
        : base(FeatureId.SwitchSession)
    {
    }

    public static string? SessionFor(int code) => code switch
    {
        _ when code == InputCodes.Digit(1) || code == InputCodes.Left || code == InputCodes.PadLeft => "公开战局",
        _ when code == InputCodes.Digit(2) || code == InputCodes.Down || code == InputCodes.PadDown => "邀请战局",
        _ when code == InputCodes.Digit(3) || code == InputCodes.Right || code == InputCodes.PadRight => "好友战局",
        _ => null,
    };

    private long _pauseDownAt = long.MinValue / 2;

    public override bool OnInput(InputEventArgs e)
    {
        if (e.Code == S.Input.Code(GameAction.Pause) || e.Code == InputCodes.PadMenu)
        {
            if (e.IsDown && !e.IsRepeat)
            {
                _pauseDownAt = e.Timestamp;
            }

            return false;
        }

        if (!e.IsDown || e.IsRepeat)
        {
            return false;
        }

        // Only while the pause key is genuinely being held (guards against a stuck key state).
        var pauseHeld = (S.InputState.IsDown(S.Input.Code(GameAction.Pause)) || S.InputState.IsDown(InputCodes.PadMenu)) &&
                        e.Timestamp - _pauseDownAt < 5000;
        var session = pauseHeld ? SessionFor(e.Code) : null;
        if (session is null)
        {
            return false;
        }

        Start(ctx =>
        {
            ctx.Tip("[切换战局] " + session);
            // Wait until the pause key is released so the game opens the pause menu cleanly.
            while (ctx.IsDown(GameAction.Pause) || ctx.IsDown(InputCodes.PadMenu))
            {
                ctx.Wait(-20);
            }

            MenuRunner.Run(ctx, session + "路径");
        });
        return true;
    }
}

/// <summary>替代回车 — Ctrl+Space / Caps+Space / middle mouse send Enter.</summary>
public sealed class EnterAlternativeFeature : MacroFeature
{
    public EnterAlternativeFeature()
        : base(FeatureId.EnterAlternative)
    {
    }

    public override bool OnInput(InputEventArgs e)
    {
        if (!e.IsDown)
        {
            return false;
        }

        var s = S.Settings;
        var hit = (e.Code == InputCodes.Space && s.GetBool(Sec.Other, "回车_启用Ctrl空格") && S.InputState.IsModifierDown(ModifierMask.Ctrl)) ||
                  (e.Code == InputCodes.Space && s.GetBool(Sec.Other, "回车_启用大写空格") && S.InputState.IsDown(InputCodes.CapsLock)) ||
                  (e.Code == InputCodes.MiddleButton && s.GetBool(Sec.Other, "回车_启用鼠标中键"));
        if (!hit)
        {
            return false;
        }

        if (!e.IsRepeat)
        {
            Start(ctx =>
            {
                var ctrlHeld = ctx.Services.InputState.IsModifierDown(ModifierMask.Ctrl);
                if (ctrlHeld)
                {
                    ctx.Input.UpCode(InputCodes.LControl);
                    ctx.Input.UpCode(InputCodes.RControl);
                }

                ctx.Tap(InputCodes.Enter, new KeyTiming(20, 10));
                if (ctrlHeld && ctx.Services.InputState.IsModifierDown(ModifierMask.Ctrl))
                {
                    ctx.Input.DownCode(InputCodes.LControl);
                }
            });
        }

        return true;
    }
}

/// <summary>挂机 — periodic AFK key presses; 空闲挂机 presses once after a long idle period.</summary>
public sealed class AfkFeature : MacroFeature
{
    private long? _lastIdlePress;
    private volatile bool _active;

    public AfkFeature()
        : base(FeatureId.Afk)
    {
    }

    public bool IsActive => _active;

    public override void OnHotkeyDown() => SetActive(!_active);

    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        if (!active)
        {
            Cancel();
            S.Notifier.Tip("[挂机] 已停止");
            return;
        }

        Start(ctx =>
        {
            var s = ctx.Settings;
            var key = s.GetInt(Sec.Other, "挂机键位");
            var hold = s.GetString(Sec.Other, "挂机方法") == "按住";
            var interval = s.GetInt(Sec.Other, "挂机点击间隔");
            ctx.Tip($"[挂机] 已开始 | {(hold ? "按住" : "点击")} {KeyNames.GetName(key)} | 间隔：{interval}");
            var held = false;
            try
            {
                if (hold)
                {
                    // Keep the key held only while GTA is in the foreground.
                    while (_active)
                    {
                        var foreground = ctx.Services.Game.IsForeground;
                        if (foreground != held)
                        {
                            if (foreground)
                            {
                                ctx.Input.DownCode(key);
                            }
                            else
                            {
                                ctx.Input.UpCode(key);
                            }

                            held = foreground;
                        }

                        ctx.Wait(-100);
                    }
                }
                else
                {
                    while (_active)
                    {
                        if (ctx.Services.Game.IsForeground)
                        {
                            ctx.Tap(key, new KeyTiming(50, 0));
                        }

                        ctx.Wait(-interval);
                    }
                }
            }
            finally
            {
                if (held)
                {
                    ctx.Input.UpCode(key);
                }

                _active = false;
            }
        });
    }

    public override void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
        {
            _active = false;
        }

        base.OnEnabledChanged(enabled);
    }

    public override void OnTick(long now)
    {
        var s = S.Settings;
        if (!s.GetBool(Sec.Other, "空闲挂机") || _active || IsRunning || !S.Game.IsForeground)
        {
            return;
        }

        var idleLimit = s.GetInt(Sec.Other, "空闲挂机时间") * 1000L;
        if (S.InputState.IdleMilliseconds < idleLimit || (_lastIdlePress is { } last && now - last < idleLimit))
        {
            return;
        }

        _lastIdlePress = now;
        var key = s.GetInt(Sec.Other, "挂机键位");
        Start(ctx =>
        {
            ctx.Log("空闲挂机：执行一次挂机按键");
            ctx.Tap(key, new KeyTiming(50, 0));
        });
    }
}

/// <summary>内置截图 — captures the game client area to the clipboard.</summary>
public sealed class ScreenshotFeature : MacroFeature
{
    public ScreenshotFeature()
        : base(FeatureId.Screenshot)
    {
    }

    public override void OnHotkeyDown()
    {
        Start(ctx =>
        {
            if (!ctx.Services.Game.IsRunning)
            {
                ctx.Tip("截图失败！游戏不存在！");
                return;
            }

            var image = ctx.Services.Screen.Capture(ctx.Services.Game.ClientBounds);
            ctx.Services.Clipboard.SetImage(image);
            ctx.Tip($"[内置截图] 已复制到剪贴板（{image.Width}x{image.Height}）");
        });
    }
}

/// <summary>外置准星 — toggles the crosshair overlay.</summary>
public sealed class CrosshairFeature : MacroFeature
{
    public CrosshairFeature()
        : base(FeatureId.Crosshair)
    {
    }

    public override bool WorksInBackground => true;

    public override void OnHotkeyDown()
    {
        if (!S.Settings.GetBool(Sec.Crosshair, "启用显示热键"))
        {
            return;
        }

        S.Overlays?.ToggleCrosshair();
    }
}

/// <summary>Hotkeys for the 小工具 system tools.</summary>
public sealed class ToolHotkeyFeature : MacroFeature
{
    public ToolHotkeyFeature(FeatureId id)
        : base(id)
    {
    }

    public override void OnHotkeyDown()
    {
        var id = Definition.Id;
        Start(ctx =>
        {
            var tools = ctx.Services.Tools;
            var result = id switch
            {
                FeatureId.KillGame => tools.KillGame(),
                FeatureId.NetworkCut => tools.ToggleNetworkCut(),
                FeatureId.SoloSession => tools.ToggleSoloSession(),
                FeatureId.SessionLock => tools.ToggleSessionLock(),
                FeatureId.SpeedLimit => tools.ToggleSpeedLimit(),
                _ => ToolResult.Fail("未知工具"),
            };
            ctx.Services.Notifier.Tip(result.Message);
        });
    }
}
