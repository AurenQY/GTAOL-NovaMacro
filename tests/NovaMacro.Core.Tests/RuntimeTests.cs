using NovaMacro.Core.Features;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Input;
using NovaMacro.Core.Link;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Tests;

public class RuntimeTests
{
    [Fact]
    public void BuyAmmoRunsDefaultPath()
    {
        var h = new Harness();
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        h.WaitIdle(h.Runtime.Get<BuyAmmoFeature>()!);
        Assert.Equal(
            new[] { "M键", "下光标键", "下光标键", "下光标键", "下光标键", "回车键", "回车键", "左光标键", "下光标键", "回车键", "M键" },
            h.Output.Taps());
    }

    [Fact]
    public void BuyCurrentAmmoWithAuxKey()
    {
        var h = new Harness();
        h.State.Down.Add(InputCodes.CapsLock);
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        h.WaitIdle(h.Runtime.Get<BuyAmmoFeature>()!);
        Assert.DoesNotContain("左光标键", h.Output.Taps());
    }

    [Fact]
    public void HotkeysDoNotFireWhenGameIsNotForeground()
    {
        var h = new Harness();
        h.Game.IsForeground = false;
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        Thread.Sleep(50);
        Assert.Empty(h.Output.Taps());
    }

    [Fact]
    public void ModifiedHotkeyIsSwallowed()
    {
        var h = new Harness();
        h.State.Down.Add(InputCodes.LAlt);
        h.State.Down.Add(InputCodes.Letter('R'));
        Assert.True(h.Runtime.HandleInput(InputCodes.Letter('R'), true));
        h.State.Down.Remove(InputCodes.Letter('R'));
        Assert.True(h.Runtime.HandleInput(InputCodes.Letter('R'), false));
        Assert.False(h.Runtime.HandleInput(InputCodes.Letter('Q'), true));
    }

    [Fact]
    public void SwallowedAltHotkeySendsMenuMaskSoGameNeverSeesLoneAlt()
    {
        var h = new Harness();
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        Assert.Equal("Mask", h.Output.Events[0]);
        h.WaitIdle(h.Runtime.Get<BuyAmmoFeature>()!);
    }

    [Fact]
    public void ArrowKeysAndEnterTriggerNothingByDefault()
    {
        var h = new Harness();
        foreach (var key in new[] { InputCodes.Up, InputCodes.Down, InputCodes.Left, InputCodes.Right, InputCodes.Enter, InputCodes.Backspace, InputCodes.Letter('M') })
        {
            h.Tap(key);
        }

        Thread.Sleep(100);
        h.Runtime.Tick();
        Assert.Empty(h.Output.Events);
        Assert.DoesNotContain(h.Notifier.Messages, m => m.StartsWith("[热键]", StringComparison.Ordinal));
    }

    [Fact]
    public void MacroWaitsForHotkeyModifierReleaseBeforeInjecting()
    {
        var h = new Harness();
        h.Press(InputCodes.Letter('R'), InputCodes.LAlt);
        Thread.Sleep(150);
        Assert.DoesNotContain(h.Output.Events, e => e.StartsWith("D:", StringComparison.Ordinal));
        h.Release(InputCodes.Letter('R'), InputCodes.LAlt);
        h.WaitIdle(h.Runtime.Get<BuyAmmoFeature>()!);
        Assert.Equal("M键", h.Output.Taps()[0]);
    }

    [Fact]
    public void ModifierStillHeldIsReleasedForTheGame()
    {
        var h = new Harness();
        h.Press(InputCodes.Letter('R'), InputCodes.LAlt);
        h.WaitIdle(h.Runtime.Get<BuyAmmoFeature>()!);
        var events = h.Output.Events;
        var altUp = events.IndexOf("U:左Alt键");
        Assert.True(altUp > 0);
        Assert.True(altUp < events.IndexOf("D:M键"));
        h.Release(InputCodes.Letter('R'), InputCodes.LAlt);
    }

    [Fact]
    public void InputRoutingIsCheapPerKeyPress()
    {
        // The keyboard hook calls HandleInput for every key press on the whole PC; it must stay in
        // the low-microsecond range so typing in other applications is never delayed.
        var h = new Harness();
        for (var i = 0; i < 2000; i++)
        {
            h.Runtime.HandleInput(InputCodes.Letter('K'), true);
            h.Runtime.HandleInput(InputCodes.Letter('K'), false);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int n = 20000;
        for (var i = 0; i < n; i++)
        {
            h.Runtime.HandleInput(InputCodes.Letter('K'), true);
            h.Runtime.HandleInput(InputCodes.Letter('K'), false);
        }

        var perEventMicroseconds = sw.Elapsed.TotalMilliseconds * 1000 / (2 * n);
        Assert.True(perEventMicroseconds < 50, $"HandleInput took {perEventMicroseconds:0.0} µs per event");
        Assert.Empty(h.Output.Events);
    }

    [Fact]
    public void DisabledFeatureDoesNothing()
    {
        var h = new Harness("[记忆开关]\n买满子弹=假\n");
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        Thread.Sleep(50);
        Assert.Empty(h.Output.Taps());
    }

    [Fact]
    public void LockBlocksMacrosUnlessWhitelisted()
    {
        var h = new Harness();
        h.Runtime.SetLocked(true);
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        Thread.Sleep(50);
        Assert.Empty(h.Output.Taps());

        h.Settings.Set(Sec.StopMacro, "宏锁定白名单", "买满子弹");
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        h.WaitIdle(h.Runtime.Get<BuyAmmoFeature>()!);
        Assert.NotEmpty(h.Output.Taps());
    }

    [Fact]
    public void ImeCheckBlocksWhenNotUsLayout()
    {
        var h = new Harness("[记忆开关]\n输入法检测防误触=真\n");
        h.Game.IsUsKeyboardLayout = false;
        h.Tap(InputCodes.Letter('R'), InputCodes.LAlt);
        Thread.Sleep(50);
        Assert.Empty(h.Output.Taps());
    }

    [Fact]
    public void StopAllCancelsAndReleases()
    {
        var h = new Harness();
        h.Runtime.StopAll();
        Assert.Contains("ReleaseAll", h.Output.Events);
        Assert.Contains(h.Notifier.Messages, m => m.Contains("停止"));
    }

    [Fact]
    public void WeaponJamRapidFireFiresAndSwitches()
    {
        var h = new Harness();
        h.Press(InputCodes.XButton1);
        var feature = h.Runtime.Get<WeaponJamFeature>()!;
        Harness.WaitFor(() => h.Output.Taps().Count >= 3);
        h.Release(InputCodes.XButton1);
        h.WaitIdle(feature);
        var taps = h.Output.Taps();
        Assert.Equal("左键", taps[0]);
        Assert.Equal("5键", taps[1]);
        Assert.Equal("4键", taps[2]);
    }

    [Fact]
    public void WeaponJamRotatesTargetWeaponWithModifier()
    {
        var h = new Harness($"[武器卡弹设置]\n切换欲切枪武器热键功能键={(int)ModifierMask.Ctrl}\n");
        h.Tap(InputCodes.XButton1, InputCodes.LControl);
        Assert.Equal("狙击步枪", h.Settings.GetString(Sec.WeaponJam, "欲切枪武器键"));
        h.Tap(InputCodes.XButton1, InputCodes.LControl);
        Assert.Equal("重型武器", h.Settings.GetString(Sec.WeaponJam, "欲切枪武器键"));
    }

    [Fact]
    public void QuickPerspectiveCyclesCamera()
    {
        var h = new Harness("[记忆开关]\n快速人称=真\n[快速人称设置]\n当前人称=0\n快速切换第三人称=第三人称远\n");
        var feature = h.Runtime.Get<QuickPerspectiveFeature>()!;
        h.Tap(InputCodes.Letter('V'), InputCodes.LControl);
        h.WaitIdle(feature);
        Assert.Equal(3, h.Output.Taps().Count(t => t == "V键"));
        Assert.Equal(3, h.Settings.GetInt(Sec.QuickPerspective, "当前人称"));
        h.Tap(InputCodes.Letter('V'), InputCodes.LControl);
        h.WaitIdle(feature);
        Assert.Equal(4, h.Output.Taps().Count(t => t == "V键"));
        Assert.Equal(0, h.Settings.GetInt(Sec.QuickPerspective, "当前人称"));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(3, 0, 1)]
    [InlineData(0, 3, 3)]
    [InlineData(2, 0, 2)]
    public void PerspectivePressCount(int from, int to, int presses) =>
        Assert.Equal(presses, QuickPerspectiveFeature.PressesBetween(from, to));

    [Fact]
    public void ServiceVehicleClickVsDoubleClickVsRotate()
    {
        var h = new Harness();
        var feature = h.Runtime.Get<ServiceVehicleFeature>()!;
        h.Tap(InputCodes.Letter('B'), InputCodes.LAlt);
        Thread.Sleep(350);
        h.Runtime.Tick();
        h.WaitIdle(feature);
        Assert.Contains(h.Notifier.Messages, m => m.Contains("呼叫虎鲸"));

        h.State.Down.Add(InputCodes.CapsLock);
        h.Tap(InputCodes.Letter('B'), InputCodes.LAlt);
        Thread.Sleep(350);
        h.Runtime.Tick();
        Assert.Equal("致幻剂实验室", h.Settings.GetString(Sec.MenuSnack, "选择的服务载具"));
        h.State.Down.Remove(InputCodes.CapsLock);

        h.Tap(InputCodes.Letter('B'), InputCodes.LAlt);
        h.Tap(InputCodes.Letter('B'), InputCodes.LAlt);
        h.WaitIdle(feature);
        Assert.Contains(h.Notifier.Messages, m => m.Contains("送回致幻剂实验室"));
    }

    [Fact]
    public void EnterAlternativeSendsEnterAndSwallowsSpace()
    {
        var h = new Harness();
        h.State.Down.Add(InputCodes.LControl);
        h.State.Down.Add(InputCodes.Space);
        Assert.True(h.Runtime.HandleInput(InputCodes.Space, true));
        Harness.WaitFor(() => h.Output.Taps().Contains("回车键"));
    }

    [Fact]
    public void SwitchSessionNeedsPauseHeld()
    {
        var h = new Harness("[记忆开关]\n切换战局=真\n");
        Assert.False(h.Runtime.HandleInput(InputCodes.Digit(1), true));

        // A stuck Esc state (no recent key-down event) must not trigger.
        h.State.Down.Add(InputCodes.Escape);
        Assert.False(h.Runtime.HandleInput(InputCodes.Digit(1), true));

        h.Runtime.HandleInput(InputCodes.Escape, true);
        Assert.True(h.Runtime.HandleInput(InputCodes.Digit(1), true));
        h.State.Down.Remove(InputCodes.Escape);
        h.WaitIdle(h.Runtime.Get<SwitchSessionFeature>()!);
        Assert.Contains(h.Notifier.Messages, m => m.Contains("公开战局"));
        Assert.Contains("E键", h.Output.Taps());
    }

    [Fact]
    public void ToolHotkeysRequireEnableFlag()
    {
        var h = new Harness();
        h.Tap(InputCodes.Function(6), InputCodes.LControl);
        Thread.Sleep(50);
        Assert.Empty(h.Tools.Calls);

        h.Settings.SetBool(Sec.Tools, "启用游戏断网热键", true);
        h.Game.IsForeground = false;
        h.Tap(InputCodes.Function(6), InputCodes.LControl);
        Harness.WaitFor(() => h.Tools.Calls.Contains("cut"));
    }

    [Fact]
    public void ScreenshotCopiesGameArea()
    {
        var h = new Harness("[记忆开关]\n内置截图=真\n");
        h.Game.ClientBounds = new ScreenRect(10, 20, 64, 48);
        h.Tap(InputCodes.Function(12), InputCodes.LControl);
        Harness.WaitFor(() => h.Clipboard.Image is not null);
        Assert.Equal((64, 48), (h.Clipboard.Image!.Width, h.Clipboard.Image.Height));
    }

    [Fact]
    public void MapTeleportSendsBotLinkThenConfirms()
    {
        var h = new Harness("[记忆开关]\n地图差传=真\n[地图差传设置]\n确认时限=1\nSteam_一号BOT={\"链接\":\"steam://rungame/3240220/76561190000000000/-steamjvp=QUJDRA==\",\"昵称\":\"测试\",\"获取时间\":\"2026-01-02 03:04:05\"}\n");
        var feature = h.Runtime.Get<MapTeleportFeature>()!;
        h.Tap(InputCodes.Function(7));
        Thread.Sleep(350);
        h.Runtime.Tick();
        Harness.WaitFor(() => h.Urls.Opened.Count == 1);
        Assert.StartsWith("steam://rungame/3240220/76561190000000000/", h.Urls.Opened[0]);
        h.WaitIdle(feature);
        Assert.Contains("回车键", h.Output.Taps());
    }

    [Fact]
    public void MapTeleportWithoutSavedLinkDoesNothing()
    {
        var h = new Harness("[记忆开关]\n地图差传=真\n");
        h.Tap(InputCodes.Function(7));
        Thread.Sleep(350);
        h.Runtime.Tick();
        h.WaitIdle(h.Runtime.Get<MapTeleportFeature>()!);
        Assert.Empty(h.Urls.Opened);
        Assert.Contains(h.Notifier.Messages, m => m.Contains("没有读取战局链接"));
    }

    [Fact]
    public void CustomMacroRunsScript()
    {
        var h = new Harness();
        var def = new Scripting.CustomMacroDefinition
        {
            Name = "t",
            Hotkey = new Hotkey(InputCodes.Function(11)),
            Script = "键 A键\n游戏 武器射击\n路径 M,↓2",
        };
        h.Runtime.ReplaceFeatures(_ => false, FeatureFactory.CreateCustom(new[] { def }));
        var feature = h.Runtime.Features.OfType<CustomMacroFeature>().Single();
        h.Tap(InputCodes.Function(11));
        h.WaitIdle(feature);
        Assert.Equal(new[] { "A键", "左键", "M键", "下光标键", "下光标键" }, h.Output.Taps());
    }

    [Fact]
    public void CustomMacroWithParseErrorIsDisabled()
    {
        var feature = new CustomMacroFeature(new Scripting.CustomMacroDefinition { Name = "bad", Script = "乱写" });
        Assert.NotNull(feature.ParseError);
        Assert.False(feature.CustomEnabled);
    }

    [Fact]
    public void AfkIdlePressesOnce()
    {
        var h = new Harness("[其他设置]\n空闲挂机=真\n空闲挂机时间=10\n");
        h.State.IdleMilliseconds = 60_000;
        h.Runtime.Tick();
        h.WaitIdle(h.Runtime.Get<AfkFeature>()!);
        Assert.Equal(new[] { "C键" }, h.Output.Taps());
    }

    [Fact]
    public void PhoneContactsParse()
    {
        var c = PhoneCallFeature.ParseContacts("Lester,-3;Mors Mutual Insurance,-7;坏");
        Assert.Equal(2, c.Count);
        Assert.Equal(("Mors Mutual Insurance", -7), c[1]);
    }

    [Fact]
    public void Scale1440CentresWideScreens()
    {
        var h = new Harness();
        h.Game.ClientBounds = new ScreenRect(0, 0, 3440, 1440);
        Assert.Equal((440, 0), h.Services.Scale1440(0, 0));
        h.Game.ClientBounds = new ScreenRect(100, 50, 1920, 1080);
        Assert.Equal((100 + 960, 50 + 540), h.Services.Scale1440(1280, 720));
    }
}

public class LinkRoomTests
{
    [Fact]
    public async Task HostAndMemberShareLinkActions()
    {
        using var host = new LinkRoom();
        using var member = new LinkRoom();
        var hostLinks = new List<string>();
        var memberLinks = new List<string>();
        host.LinkTriggered += (_, m) => { lock (hostLinks) { hostLinks.Add(m.Kind!); } };
        member.LinkTriggered += (_, m) => { lock (memberLinks) { memberLinks.Add(m.Kind!); } };
        var port = host.Host(0, "房主");
        await member.JoinAsync("127.0.0.1:" + port, "成员");
        Harness.WaitFor(() => host.Members.Count == 2);

        await member.TriggerLinkAsync(LinkKinds.Solo);
        Harness.WaitFor(() => hostLinks.Count == 1 && memberLinks.Count == 1);
        Assert.Equal(LinkKinds.Solo, hostLinks[0]);

        await host.TriggerLinkAsync(LinkKinds.NetworkCut);
        Harness.WaitFor(() => memberLinks.Count == 2);
        Assert.Equal(LinkKinds.NetworkCut, memberLinks[1]);
    }

    [Fact]
    public async Task BlacklistedMemberIsRejected()
    {
        using var host = new LinkRoom();
        using var member = new LinkRoom();
        var statuses = new List<string>();
        member.Status += (_, s) => { lock (statuses) { statuses.Add(s); } };
        host.SetBlacklist(new[] { "坏人" });
        var port = host.Host(0, "房主");
        await member.JoinAsync("127.0.0.1:" + port, "坏人");
        Harness.WaitFor(() => { lock (statuses) { return statuses.Contains("[你已被拉黑]"); } });
    }

    [Fact]
    public void AddressParsing()
    {
        Assert.Equal(("1.2.3.4", 99), LinkRoom.ParseAddress("1.2.3.4:99"));
        Assert.Equal(("host", 19730), LinkRoom.ParseAddress("host"));
    }
}
