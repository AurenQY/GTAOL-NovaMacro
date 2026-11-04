using System.Diagnostics;
using NovaMacro.Core.Game;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Input;
using NovaMacro.Core.Macros;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;
using NovaMacro.Core.Steam;

namespace NovaMacro.Core.Features;

/// <summary>
/// 地图差传 — joins a BOT session (Steam join link or Steam friends list "加入游戏") and then
/// confirms the join at the right moment (BOT法: continuous confirm stage, 读秒法: timed, 听声法: audio cue).
/// </summary>
public sealed class MapTeleportFeature : MacroFeature
{
    private volatile bool _anyKeyPressed;

    public MapTeleportFeature()
        : base(FeatureId.MapTeleport)
    {
    }

    public override bool UsesGestures => true;

    public static string BotKey(string bot) => bot == "Steam_二号BOT" ? "Steam_二号BOT" : "Steam_一号BOT";

    public string BotFolder => Path.Combine(S.DataFolder, "MapTeleportBot");

    public override bool OnInput(InputEventArgs e)
    {
        if (e.IsDown && IsRunning)
        {
            _anyKeyPressed = true;
        }

        return false;
    }

    public override void OnGesture(PressGesture gesture)
    {
        if (IsRunning)
        {
            Cancel();
            S.Notifier.Tip("[地图差传_BOT法] 已停止差传");
            return;
        }

        var trigger = S.Settings.GetString(Sec.MapTeleport, "触发方式");
        if (trigger == "长按" && gesture != PressGesture.LongPress)
        {
            return;
        }

        if (trigger != "长按" && gesture == PressGesture.LongPress)
        {
            return;
        }

        if (gesture == PressGesture.DoubleClick && S.IsAuxDown)
        {
            ReadLatestLink(BotKey(S.Settings.GetString(Sec.MapTeleport, "差传BOT")));
            return;
        }

        Start(Run);
    }

    /// <summary>Reads the newest join link from Steam's console log into a BOT slot.</summary>
    public SteamJoinLink? ReadLatestLink(string botKey)
    {
        var steamPath = S.Steam?.InstallPath;
        if (steamPath is null)
        {
            S.Notifier.ShowMessage("读取失败！", "Steam未运行！您似乎不是steam玩家？");
            return null;
        }

        var log = Path.Combine(steamPath, "logs", "console_log.txt");
        if (!File.Exists(log))
        {
            S.Notifier.ShowMessage("战局链接获取失败！", "找不到最近加入的战局链接，请手动加入一次后再次读取！\n（注意：必须为外部Steam聊天窗口加入）");
            return null;
        }

        string text;
        using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        var link = SteamJoinLink.LatestFromConsoleLog(text, botKey);
        if (link is null)
        {
            S.Notifier.ShowMessage("战局链接获取失败！", "找不到最近加入的战局链接，请手动加入一次后再次读取！\n（注意：必须为外部Steam聊天窗口加入）");
            return null;
        }

        S.Settings.Set(Sec.MapTeleport, botKey, link.ToJson());
        S.Settings.Save();
        S.Notifier.Tip($"战局链接读取完成！【{botKey}】{link.EditionName}");
        return link;
    }

    private void Run(MacroContext ctx)
    {
        var s = ctx.Settings;
        var method = s.GetString(Sec.MapTeleport, "选择方法");
        var botKey = BotKey(s.GetString(Sec.MapTeleport, "差传BOT"));
        var prefix = $"[地图差传_{method}]";
        var sw = Stopwatch.StartNew();

        if (!SendJoin(ctx, botKey, prefix))
        {
            return;
        }

        switch (method)
        {
            case "读秒法":
                var seconds = s.GetInt(Sec.MapTeleport, "读秒法等待");
                ctx.Tip($"{prefix} 等待 {seconds} 秒...");
                ctx.Wait(seconds * 1000);
                break;
            case "听声法":
                if (!WaitForAudioCue(ctx, prefix))
                {
                    return;
                }

                break;
        }

        ConfirmStage(ctx, prefix);
        ctx.Log($"{prefix} 已执行：{sw.Elapsed.TotalSeconds:0.0} 秒");
    }

    private bool SendJoin(MacroContext ctx, string botKey, string prefix)
    {
        var s = ctx.Settings;
        if (s.GetBool(Sec.MapTeleport, "Steam点击加入"))
        {
            return ClickJoin(ctx, prefix);
        }

        var link = SteamJoinLink.FromJson(s.GetString(Sec.MapTeleport, botKey));
        if (link is null || !link.IsValid)
        {
            ctx.Tip($"{prefix} 当前选择的Steam BOT没有读取战局链接！请前往设置进行读取链接。");
            if (!s.GetBool(Sec.MapTeleport, "隐藏提示"))
            {
                ctx.Services.Notifier.ShowMessage("加入错误", "当前选择的Steam BOT没有读取战局链接！请前往设置进行读取。\n如何读取：打开【外部】Steam群组聊天窗口，之后手动加入一次BOT战局（可不确认加入），之后回到界面点击读取即可");
            }

            return false;
        }

        if (ctx.Services.Steam is { IsRunning: false })
        {
            ctx.Tip($"{prefix} Steam未运行！");
            return false;
        }

        ctx.Services.Urls.Open(link.Url);
        ctx.Tip($"{prefix} 已发送加入 【{(link.Nickname.Length > 0 ? link.Nickname : botKey)}】 战局链接");
        return true;
    }

    private bool ClickJoin(MacroContext ctx, string prefix)
    {
        var s = ctx.Settings;
        var services = ctx.Services;
        var botImage = services.Screen.LoadImage(Path.Combine(BotFolder, "BOT1.BMP"));
        var joinImage = services.Screen.LoadImage(Path.Combine(BotFolder, "JOIN.BMP"));
        if (botImage is null)
        {
            ctx.Tip("未添加好友位参考图片！请前往 '设置-按键宏设置' 添加差传参考图片！");
            return false;
        }

        if (s.GetBool(Sec.MapTeleport, "预载"))
        {
            ctx.Log($"{prefix} Home预载中...");
            ctx.Press(GameAction.SocialClubHome, new KeyTiming(30, 30), 2);
            ctx.Wait(3500);
            ctx.Log($"{prefix} Home预载完成");
        }

        services.Urls.Open("steam://open/friends");
        ctx.Tip($"{prefix} 找图开始！按住大写键暂停找图，按住鼠标右键停止找图。");
        var deadline = Stopwatch.StartNew();
        TemplateMatcher.Match? found = null;
        var screen = services.Screen.VirtualScreen;
        while (deadline.ElapsedMilliseconds < 15000)
        {
            if (ctx.IsDown(InputCodes.RightButton))
            {
                ctx.Tip($"{prefix} 已停止差传");
                return false;
            }

            if (ctx.IsDown(InputCodes.CapsLock))
            {
                ctx.Wait(-100);
                continue;
            }

            found = TemplateMatcher.Find(services.Screen.Capture(screen), botImage);
            if (found is not null)
            {
                break;
            }

            if (s.GetBool(Sec.MapTeleport, "翻找好友列表"))
            {
                services.Input.Output.Wheel(-3);
            }

            ctx.Wait(-200);
        }

        if (found is null)
        {
            ctx.Tip($"{prefix} 未找到好友位位置！请查看参考图片是否需要修改或位置被遮挡。");
            if (s.GetBool(Sec.MapTeleport, "主动寻找"))
            {
                ctx.Tap(InputCodes.LeftButton, new KeyTiming(30, 30));
            }

            return false;
        }

        var x = screen.X + found.X + (botImage.Width / 2);
        var y = screen.Y + found.Y + (botImage.Height / 2);
        services.Input.Click(x, y, InputCodes.RightButton, new KeyTiming(30, 300), ctx.Token);
        if (joinImage is not null)
        {
            var join = TemplateMatcher.Find(services.Screen.Capture(screen), joinImage);
            if (join is null)
            {
                ctx.Tip($"{prefix} 未找到加入游戏按钮！请查看参考图片是否需要修改或位置被遮挡。");
                return false;
            }

            services.Input.Click(screen.X + join.X + (joinImage.Width / 2), screen.Y + join.Y + (joinImage.Height / 2), InputCodes.LeftButton, new KeyTiming(30, 300), ctx.Token);
        }

        ctx.Tip($"{prefix} 已点击加入 【{s.GetString(Sec.MapTeleport, "点击加入名称")}】");
        services.Game.Activate();
        return true;
    }

    private bool WaitForAudioCue(MacroContext ctx, string prefix)
    {
        var audio = ctx.Services.Audio;
        if (audio is null || !audio.Start())
        {
            ctx.Tip($"{prefix} 无法捕获声音，自动换为读秒法");
            ctx.Wait(ctx.Settings.GetInt(Sec.MapTeleport, "读秒法等待") * 1000);
            return true;
        }

        try
        {
            var threshold = ctx.Settings.GetInt(Sec.MapTeleport, "听声阈值") / 100f;
            var pause = ctx.Settings.GetInt(Sec.MapTeleport, "听声停顿");
            var heardSound = false;
            var quietSince = Stopwatch.StartNew();
            var limit = Stopwatch.StartNew();
            ctx.Tip($"{prefix} 地图差传听声捕捉中");
            while (limit.Elapsed.TotalSeconds < 120)
            {
                var level = audio.Level;
                if (level >= threshold)
                {
                    heardSound = true;
                    quietSince.Restart();
                }
                else if (heardSound && quietSince.ElapsedMilliseconds >= pause)
                {
                    ctx.Log($"{prefix} 音频：{level:0.00} | 停顿：{quietSince.ElapsedMilliseconds}");
                    return true;
                }

                ctx.Wait(-20);
            }

            ctx.Tip($"{prefix} 听声超时");
            return false;
        }
        finally
        {
            audio.Stop();
        }
    }

    private void ConfirmStage(MacroContext ctx, string prefix)
    {
        var seconds = ctx.Settings.GetInt(Sec.MapTeleport, "确认时限");
        _anyKeyPressed = false;
        ctx.Log("-------------------------------------------------------------");
        ctx.Tip($"{prefix} 正在进行{seconds}秒时限的确认阶段，按住任意键停止...");
        ctx.Services.Game.Activate();
        var sw = Stopwatch.StartNew();
        var confirms = 0;
        var t = new KeyTiming(30, 30);
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            if (_anyKeyPressed)
            {
                ctx.Tip($"{prefix} 你按下了 按键，停止确认");
                break;
            }

            if (ctx.Services.Game.IsForeground)
            {
                ctx.Press(GameAction.MenuSelect, t);
                confirms++;
            }

            ctx.Wait(-400);
        }

        ctx.Log($"{prefix} 已确认 {confirms} 次 | 截至最后一次确认已执行：{sw.Elapsed.TotalSeconds:0.0} 秒");
    }
}

/// <summary>任务卡传 — quick-join a random job, wait for matchmaking, then register as CEO/MC.</summary>
public sealed class JobTeleportFeature : MacroFeature
{
    private volatile bool _skip;

    public JobTeleportFeature()
        : base(FeatureId.JobTeleport)
    {
    }

    public override bool OnInput(InputEventArgs e)
    {
        if (IsRunning && e.IsDown && e.Code == S.Input.Code(GameAction.InteractionMenu))
        {
            _skip = true;
            return true;
        }

        return false;
    }

    public override void OnHotkeyDown()
    {
        var direct = S.IsAuxDown;
        _skip = false;
        Start(ctx =>
        {
            var s = ctx.Settings;
            var role = s.GetString(Sec.JobTeleport, "注册身份");
            if (!direct)
            {
                ctx.Tip("[任务卡传] 快速加入随机差事...");
                MenuRunner.Run(ctx, "快速加入差事路径");
                ctx.Tip($"[任务卡传] 正在进行判断匹配差事（再次触发M菜单键跳过）... 按住 [{KeyNames.GetName(ctx.Input.Code(GameAction.InteractionMenu))}] 跳过判断");
                var sw = Stopwatch.StartNew();
                var wait = s.GetInt(Sec.JobTeleport, "判断等待");
                while (!_skip && sw.ElapsedMilliseconds < wait)
                {
                    ctx.Wait(-50);
                }

                ctx.Log("判断成功！");
            }

            ctx.Log("注册" + role);
            MenuRunner.Run(ctx, role == "摩托帮" ? "注册摩托帮路径" : "注册CEO路径");
        });
    }
}
