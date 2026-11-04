using NovaMacro.Core.Game;
using NovaMacro.Core.Macros;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Features;

/// <summary>买满子弹 — buys all ammo (aux held: current weapon ammo).</summary>
public sealed class BuyAmmoFeature : MacroFeature
{
    public BuyAmmoFeature()
        : base(FeatureId.BuyAmmo)
    {
    }

    public override void OnHotkeyDown()
    {
        var currentOnly = S.IsAuxDown;
        Start(ctx =>
        {
            if (currentOnly)
            {
                ctx.Log("购买当前武器子弹");
                MenuRunner.Run(ctx, "买当前子弹路径");
            }
            else
            {
                ctx.Log(ctx.Settings.GetBool(Sec.MenuSnack, "启用自适应买所有子弹") ? "购买全部子弹（自适应）" : "购买全部子弹（默认左选）");
                MenuRunner.Run(ctx, "买满子弹路径");
            }
        });
    }
}

/// <summary>菜单零食 — eats snacks via the interaction menu; aux held: puts on body armor.</summary>
public sealed class MenuSnackFeature : MacroFeature
{
    public MenuSnackFeature()
        : base(FeatureId.MenuSnack)
    {
    }

    public override void OnHotkeyDown()
    {
        var armor = S.IsAuxDown;
        Start(ctx =>
        {
            if (armor)
            {
                var type = ctx.Settings.GetString(Sec.MenuSnack, "要穿的护甲");
                var index = Math.Max(0, SettingsSchema.ArmorTypes.ToList().IndexOf(type) - 1);
                ctx.Log(type == "自动识别" ? "自动识别可用防弹衣..." : "穿戴：" + type);
                MenuRunner.Run(ctx, "护甲路径", n => n == "护甲" ? index : null);
                return;
            }

            var count = ctx.Settings.GetInt(Sec.MenuSnack, "吃零食个数");
            var interval = ctx.Settings.GetInt(Sec.MenuSnack, "吃零食间隔");
            var timing = MenuRunner.MenuTiming(ctx.Settings);
            MenuRunner.Run(ctx, "零食路径", onMark: mark =>
            {
                if (mark != "吃零食")
                {
                    return;
                }

                if (count <= 0)
                {
                    ctx.Log($"持续吃零食，间隔：{interval}毫秒");
                    do
                    {
                        ctx.Press(GameAction.MenuSelect, timing);
                        ctx.Wait(interval);
                    }
                    while (HotkeyHeld);
                }
                else
                {
                    ctx.Log($"吃 {count} 个零食");
                    for (var i = 0; i < count; i++)
                    {
                        ctx.Press(GameAction.MenuSelect, timing);
                        ctx.Wait(interval);
                    }
                }
            });
        });
    }
}

/// <summary>载具启动 — personal vehicle request/return and engine/door control.</summary>
public sealed class VehicleControlFeature : MacroFeature
{
    public VehicleControlFeature()
        : base(FeatureId.VehicleControl)
    {
    }

    public override bool UsesGestures => true;

    public override void OnGesture(PressGesture gesture)
    {
        var aux = S.IsAuxDown;
        Start(ctx =>
        {
            string op;
            if (aux)
            {
                // Original: aux + click returns the vehicle, aux + double click requests it.
                op = gesture == PressGesture.DoubleClick ? "叫出载具" : "收回载具";
            }
            else
            {
                op = ctx.Settings.GetString(Sec.MenuSnack, "载具启动操作");
            }

            var path = op switch
            {
                "控制车门" => "控制车门路径",
                "控制全部" => "控制全部路径",
                "叫出载具" => "叫出载具路径",
                "收回载具" => "收回载具路径",
                _ => "控制引擎路径",
            };
            ctx.Log(op);
            var steps = MenuRunner.Load(ctx.Settings, path).ToList();
            if (op == "控制引擎" && ctx.Settings.GetBool(Sec.MenuSnack, "载具启动停留引擎界面") && steps.Count > 0 && steps[^1].Action == GameAction.InteractionMenu)
            {
                steps.RemoveAt(steps.Count - 1);
            }

            MenuRunner.Run(ctx, steps);
        });
    }
}

/// <summary>呼叫载具 — requests/returns service vehicles (MOC, Avenger, Terrorbyte, Kosatka, Acid Lab…).</summary>
public sealed class ServiceVehicleFeature : MacroFeature
{
    public ServiceVehicleFeature()
        : base(FeatureId.ServiceVehicle)
    {
    }

    public override bool UsesGestures => true;

    public string Selected => S.Settings.GetString(Sec.MenuSnack, "选择的服务载具");

    public IReadOnlyList<string> Enabled
    {
        get
        {
            var list = S.Settings.GetString(Sec.MenuSnack, "启用的服务载具")
                .Split(new[] { ',', '，', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => SettingsSchema.ServiceVehicles.Contains(v))
                .ToList();
            return list.Count > 0 ? list : SettingsSchema.ServiceVehicles;
        }
    }

    public void Rotate()
    {
        var enabled = Enabled;
        var index = enabled.ToList().IndexOf(Selected);
        var next = enabled[(index + 1) % enabled.Count];
        S.Settings.Set(Sec.MenuSnack, "选择的服务载具", next);
        S.Notifier.Tip("[呼叫载具] 已轮换到" + next);
    }

    public override void OnGesture(PressGesture gesture)
    {
        if (gesture == PressGesture.Click && S.IsAuxDown)
        {
            Rotate();
            return;
        }

        var vehicle = Selected;
        Start(ctx =>
        {
            var index = ctx.Settings.GetInt(Sec.MenuLayout, vehicle + "序号", 0);
            if (vehicle == "个人载具")
            {
                ctx.Log(gesture == PressGesture.DoubleClick ? "送回个人载具" : "呼叫个人载具");
                MenuRunner.Run(ctx, gesture == PressGesture.DoubleClick ? "收回载具路径" : "叫出载具路径");
                return;
            }

            var (path, text) = gesture switch
            {
                PressGesture.DoubleClick => ("送回服务载具路径", $"送回{vehicle}"),
                PressGesture.LongPress => ("呼叫内部载具路径", $"呼叫内部载具（{vehicle}）"),
                _ => ("呼叫服务载具路径", $"呼叫{vehicle}"),
            };
            ctx.Log(text);
            MenuRunner.Run(ctx, path, n => n == "载具" ? index : null);
        });
    }
}
