using System.Diagnostics;
using NovaMacro.Core.Game;
using NovaMacro.Core.Heists;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Input;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Features;

/// <summary>赌场豪劫 — automatic fingerprint matching for the Diamond Casino hack.</summary>
public sealed class CasinoHeistFeature : MacroFeature
{
    public CasinoHeistFeature()
        : base(FeatureId.CasinoHeist)
    {
    }

    public override void OnHotkeyDown()
    {
        Start(ctx =>
        {
            var services = ctx.Services;
            if (!services.Game.IsRunning)
            {
                ctx.Tip("你没有运行游戏！");
                return;
            }

            var sw = Stopwatch.StartNew();
            var bounds = services.Game.ClientBounds;
            ctx.Log($"开始匹配！（{bounds.Width}x{bounds.Height}）");
            var frame = services.Screen.Capture(bounds);
            if (ctx.Settings.GetString(Sec.Casino, "处理方式") == "文件处理")
            {
                // Keep the captured frame for checking/adjusting the layout.
                services.Screen.SaveImage(frame, Path.Combine(services.DataFolder, "Casino_fingerprint.bmp"));
            }
            var hashes = new List<ulong>();
            for (var i = 0; i < CasinoFingerprintSolver.Columns * CasinoFingerprintSolver.Rows; i++)
            {
                var (x, y, w, h) = CasinoFingerprintSolver.CellRect(i);
                var rect = services.Scale1440(x, y, w, h).Offset(-bounds.X, -bounds.Y);
                hashes.Add(CasinoFingerprintSolver.HashElement(frame.Crop(rect.X, rect.Y, rect.Width, rect.Height)));
            }

            var max = ctx.Settings.GetInt(Sec.Casino, "指纹匹配阈值");
            var solution = CasinoFingerprintSolver.Solve(hashes, max);
            if (!solution.Success)
            {
                ctx.Tip($"[赌场指纹] 匹配失败！（最低差异值：{solution.Worst}）");
                return;
            }

            ctx.Log($"匹配成功！当前匹配的指纹位置及最低差异值：指纹 {solution.Group + 1} | 位置 {string.Join(",", solution.Selected.Select(s => s + 1))} | 差异 {solution.Worst}");
            var t = new KeyTiming(ctx.Settings.GetInt(Sec.Casino, "赌场指纹按键延迟"), ctx.Settings.GetInt(Sec.Casino, "赌场指纹按键间隔"));
            foreach (var key in CasinoFingerprintSolver.KeySequence(solution.Selected))
            {
                ctx.Tap(key, t);
            }

            ctx.Tip($"[赌场指纹] 匹配结束！总用时：{sw.Elapsed.TotalSeconds:0.00} 秒");
        });
    }
}

/// <summary>佩里科岛 — fingerprint scanner solver and 自动梅杜莎 autopilot.</summary>
public sealed class CayoPericoFeature : MacroFeature
{
    public CayoPericoFeature()
        : base(FeatureId.CayoPerico)
    {
    }

    public override void OnHotkeyDown()
    {
        if (IsRunning)
        {
            Cancel();
            S.Notifier.Tip("[自动梅杜莎] 已紧急停止！");
            return;
        }

        Start(ctx =>
        {
            if (!ctx.Services.Game.IsRunning)
            {
                ctx.Tip("你没有运行游戏！");
                return;
            }

            var minimap = CaptureMinimap(ctx);
            var waypoint = ctx.Settings.GetInt(Sec.Cayo, "紫色标点颜色");
            var horizon = ctx.Settings.GetInt(Sec.Cayo, "飞机水平线颜色");
            var tolerance = ctx.Settings.GetInt(Sec.Cayo, "颜色容差");
            var decision = MedusaAutopilot.Decide(minimap, waypoint, horizon, tolerance);
            if (decision.WaypointFound && decision.AltitudeFraction is not null)
            {
                Autopilot(ctx);
            }
            else
            {
                SolveFingerprint(ctx);
            }
        });
    }

    private static PixelBuffer CaptureMinimap(MacroContext ctx)
    {
        var rect = ScreenRectD.Parse(ctx.Settings.GetString(Sec.Cayo, "小地图区域"), MedusaAutopilot.MinimapRect);
        return ctx.Services.Screen.Capture(ctx.Services.Scale1440(rect.X, rect.Y, rect.W, rect.H));
    }

    private static void Autopilot(MacroContext ctx)
    {
        var s = ctx.Settings;
        var waypoint = s.GetInt(Sec.Cayo, "紫色标点颜色");
        var horizon = s.GetInt(Sec.Cayo, "飞机水平线颜色");
        var tolerance = s.GetInt(Sec.Cayo, "颜色容差");
        var limit = TimeSpan.FromSeconds(s.GetInt(Sec.Cayo, "梅杜莎最长时间"));
        var sw = Stopwatch.StartNew();
        ctx.Tip("[自动梅杜莎] 发现紫色标点！开始自动飞行！（可再次按住热键来紧急停止！）");
        ctx.Input.Down(GameAction.PlaneThrottleUp);
        var missing = 0;
        try
        {
            while (sw.Elapsed < limit)
            {
                var d = MedusaAutopilot.Decide(CaptureMinimap(ctx), waypoint, horizon, tolerance);
                if (d.Arrived)
                {
                    ctx.Tip("[自动梅杜莎] 已抵达目的地！");
                    return;
                }

                if (!d.WaypointFound)
                {
                    if (++missing > 40)
                    {
                        ctx.Tip("未发现紫色标点！可能没有主动标点或地图不是缩小的状态！（或颜色不对应，请重新设置）");
                        return;
                    }
                }
                else
                {
                    missing = 0;
                }

                switch (d.Yaw)
                {
                    case YawCommand.Left:
                        ctx.Press(GameAction.PlaneYawLeft, new KeyTiming(Math.Abs(d.HeadingErrorDegrees) > 30 ? 200 : 100, 0));
                        break;
                    case YawCommand.Right:
                        ctx.Press(GameAction.PlaneYawRight, new KeyTiming(Math.Abs(d.HeadingErrorDegrees) > 30 ? 200 : 100, 0));
                        break;
                }

                switch (d.Pitch)
                {
                    case PitchCommand.Climb:
                        ctx.Press(GameAction.PlanePitchUp, new KeyTiming(200, 0));
                        break;
                    case PitchCommand.Dive:
                        ctx.Log("####################超出高空区域！####################");
                        ctx.Press(GameAction.PlanePitchDown, new KeyTiming(500, 0));
                        break;
                }

                ctx.Wait(-60);
            }

            ctx.Tip("[自动梅杜莎] 操作已结束（超时）");
        }
        finally
        {
            ctx.Input.Up(GameAction.PlaneThrottleUp);
        }
    }

    private static void SolveFingerprint(MacroContext ctx)
    {
        var s = ctx.Settings;
        var sw = Stopwatch.StartNew();
        var (target, puzzle) = CayoFingerprintSolver.ParseLayout(s.GetString(Sec.Cayo, "指纹目标区域"), s.GetString(Sec.Cayo, "指纹拼图区域"));
        var t = new KeyTiming(s.GetInt(Sec.Cayo, "佩岛指纹按键延迟"), s.GetInt(Sec.Cayo, "佩岛指纹按键间隔"));
        var settle = s.GetInt(Sec.Cayo, "指纹旋转等待");
        var screen = ctx.Services.Screen;
        var targetRect = ctx.Services.Scale1440(target.X, target.Y, target.W, target.H);
        var puzzleRect = ctx.Services.Scale1440(puzzle.X, puzzle.Y, puzzle.W, puzzle.H);
        ctx.Log("[佩岛指纹] 开始匹配！(" + s.GetString(Sec.Cayo, "处理方式", "内存-Bitmap") + ")");
        var targetImage = screen.Capture(targetRect);
        for (var slice = 0; slice < CayoFingerprintSolver.Slices; slice++)
        {
            var targetBand = CayoFingerprintSolver.Band(targetImage, slice);
            var diffs = new List<double>();
            for (var v = 0; v < CayoFingerprintSolver.Variants; v++)
            {
                var band = CayoFingerprintSolver.Band(screen.Capture(puzzleRect), slice);
                diffs.Add(CayoFingerprintSolver.Difference(band, targetBand));
                ctx.Tap(InputCodes.Right, t);
                ctx.Wait(settle);
            }

            // After 8 presses we are back at the starting variant; rotate to the best one.
            var best = CayoFingerprintSolver.BestRotation(diffs);
            for (var i = 0; i < best; i++)
            {
                ctx.Tap(InputCodes.Right, t);
            }

            if (slice < CayoFingerprintSolver.Slices - 1)
            {
                ctx.Tap(InputCodes.Down, t);
            }
        }

        ctx.Tip($"[佩岛指纹] 匹配完成！总用时：{sw.Elapsed.TotalSeconds:0.00}秒");
    }
}
