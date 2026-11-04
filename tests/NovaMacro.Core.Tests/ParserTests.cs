using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Macros;
using NovaMacro.Core.Network;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Scripting;
using NovaMacro.Core.Steam;
using NovaMacro.Core.Timing;

namespace NovaMacro.Core.Tests;

public class GtaControlFileTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rage__ControlInput__ControlSettings>
          <Mappings>
            <Item><Input>INPUT_INTERACTION_MENU</Input><Source>IOMS_KEYBOARD</Source><Parameters><Item>KEY_F1</Item></Parameters></Item>
            <Item><Input>INPUT_ATTACK</Input><Source>IOMS_MOUSE_BUTTON</Source><Parameters><Item>MOUSE_LEFT</Item></Parameters></Item>
            <Item><Input>INPUT_SELECT_WEAPON_HEAVY</Input><Source>IOMS_KEYBOARD</Source><Parameters><Item>KEY_NUMPAD4</Item></Parameters></Item>
            <Item><Input>INPUT_SELECT_WEAPON_HEAVY</Input><Source>IOMS_KEYBOARD</Source><Parameters><Item>KEY_Z</Item></Parameters></Item>
            <Item><Input>INPUT_CELLPHONE_SCROLL_FORWARD</Input><Source>IOMS_MOUSE_WHEEL</Source><Parameters><Item>IOM_WHEEL_DOWN</Item></Parameters></Item>
            <Item><Input>INPUT_JUMP</Input><Source>IOMS_PAD_DIGITALBUTTON</Source><Parameters><Item>KEY_X</Item></Parameters></Item>
          </Mappings>
        </rage__ControlInput__ControlSettings>
        """;

    [Fact]
    public void ReadsPrimaryBindings()
    {
        var map = GtaControlFile.Parse(Sample);
        Assert.Equal(InputCodes.Function(1), map[GameAction.InteractionMenu]);
        Assert.Equal(InputCodes.LeftButton, map[GameAction.Attack]);
        Assert.Equal(InputCodes.Numpad0 + 4, map[GameAction.WeaponHeavy]);
        Assert.Equal(InputCodes.WheelDown, map[GameAction.PhoneScrollForward]);
        Assert.False(map.ContainsKey(GameAction.Jump));
    }

    [Fact]
    public void PrefersSecondaryWhenAsked()
    {
        var map = GtaControlFile.Parse(Sample, preferSecondary: true);
        Assert.Equal(InputCodes.Letter('Z'), map[GameAction.WeaponHeavy]);
    }

    [Fact]
    public void KeyMapAppliesOverridesAndDescribes()
    {
        var keys = new GameKeyMap();
        Assert.Equal(InputCodes.Letter('M'), keys[GameAction.InteractionMenu]);
        keys.Apply(GtaControlFile.Parse(Sample), "profile");
        Assert.Equal(InputCodes.Function(1), keys[GameAction.InteractionMenu]);
        Assert.Contains("M菜单呼出 | 键值：F1键", keys.Describe());
    }

    [Fact]
    public void InvalidXmlYieldsNothing() => Assert.Empty(GtaControlFile.Parse("<not xml"));

    [Fact]
    public void EditionFromExe()
    {
        Assert.Equal(GtaEdition.Enhanced, GtaInstall.EditionFromExe("GTA5_Enhanced.exe"));
        Assert.Equal(GtaEdition.Legacy, GtaInstall.EditionFromExe(@"C:\x\GTA5.exe"));
        Assert.Equal(GtaEdition.Unknown, GtaInstall.EditionFromExe("notepad.exe"));
    }
}

public class MenuPathTests
{
    [Fact]
    public void ParsesDirectionsCountsAndPlaceholders()
    {
        var steps = MenuPath.Parse("M,↓{位置},确认,↑2,←,等待150,键:E键*2,#标记,返回", n => n == "位置" ? 4 : null);
        Assert.Equal(GameAction.InteractionMenu, steps[0].Action);
        Assert.Equal((GameAction.MenuDown, 4), (steps[1].Action, steps[1].Count));
        Assert.Equal(GameAction.MenuSelect, steps[2].Action);
        Assert.Equal((GameAction.MenuUp, 2), (steps[3].Action, steps[3].Count));
        Assert.Equal(GameAction.MenuLeft, steps[4].Action);
        Assert.Equal(150, steps[5].Milliseconds);
        Assert.Equal((MenuStepKind.Key, InputCodes.Letter('E'), 2), (steps[6].Kind, steps[6].Code, steps[6].Count));
        Assert.Equal("标记", steps[7].Mark);
        Assert.Equal(GameAction.MenuBack, steps[8].Action);
    }

    [Fact]
    public void NegativePositionMovesUpAndZeroIsSkipped()
    {
        var steps = MenuPath.Parse("↓{a},↓{b}", n => n == "a" ? -3 : 0);
        Assert.Single(steps);
        Assert.Equal((GameAction.MenuUp, 3), (steps[0].Action, steps[0].Count));
    }

    [Fact]
    public void AllDefaultPathsParse()
    {
        foreach (var d in Settings.SettingsSchema.All.Where(d => d.Section == Settings.Sec.MenuLayout && d.Key.EndsWith("路径", StringComparison.Ordinal)))
        {
            Assert.NotEmpty(MenuPath.Parse(d.Default, _ => 1));
        }
    }
}

public class ScriptTests
{
    [Fact]
    public void ParsesAllCommandKinds()
    {
        var c = MacroScript.Parse("""
            // comment
            键 A键 点击 30 20 2
            游戏 武器射击 按下 延迟=5
            鼠标 侧键上
            移动 960,540
            等待 1.5秒
            等待 200
            路径 M,↓4,确认
            定位 生命值与弹药
            key E键 up
            """);
        Assert.Equal(9, c.Count);
        Assert.Equal((ScriptCommandKind.Key, InputCodes.Letter('A'), 30, 20, 2), (c[0].Kind, c[0].Code, c[0].Interval, c[0].Delay, c[0].Times));
        Assert.Equal((ScriptCommandKind.GameKey, GameAction.Attack, KeyAction.Down, 5), (c[1].Kind, c[1].Action, c[1].KeyAction, c[1].Delay));
        Assert.Equal(InputCodes.XButton2, c[2].Code);
        Assert.Equal((960, 540), (c[3].X, c[3].Y));
        Assert.Equal(1500, c[4].Milliseconds);
        Assert.Equal(200, c[5].Milliseconds);
        Assert.Equal(ScriptCommandKind.Path, c[6].Kind);
        Assert.Equal(ScriptCommandKind.Position, c[7].Kind);
        Assert.Equal(KeyAction.Up, c[8].KeyAction);
    }

    [Theory]
    [InlineData("跳舞 A键", 1)]
    [InlineData("键 不存在键", 1)]
    [InlineData("\n鼠标 A键", 2)]
    [InlineData("等待 abc", 1)]
    [InlineData("移动 1", 1)]
    public void ReportsErrorLine(string script, int line)
    {
        var ex = Assert.Throws<ScriptParseException>(() => MacroScript.Parse(script));
        Assert.Equal(line, ex.Line);
        Assert.Contains("发现错误代码", ex.Message);
    }

    [Fact]
    public void CustomMacroXmlRoundTrip()
    {
        var def = new CustomMacroDefinition
        {
            Name = "测试",
            Category = "分类",
            Enabled = false,
            Loop = true,
            Unlocked = true,
            Trigger = CustomTrigger.Toggle,
            Hotkey = new Hotkey(InputCodes.Letter('K'), ModifierMask.Alt, InputCodes.CapsLock),
            Script = "键 A键\n<特殊&字符>",
        };
        var back = CustomMacroDefinition.FromXml(def.ToXml(), def.Name, def.Category);
        Assert.Equal((def.Enabled, def.Loop, def.Unlocked, def.Trigger, def.Hotkey, def.Script), (back.Enabled, back.Loop, back.Unlocked, back.Trigger, back.Hotkey, back.Script));
    }

    [Fact]
    public void SaveAndLoadAllFromFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        foreach (var s in CustomMacroDefinition.Starters)
        {
            s.Save(folder);
        }

        var loaded = CustomMacroDefinition.LoadAll(folder);
        Assert.Equal(CustomMacroDefinition.Starters.Count, loaded.Count);
        Assert.Contains(loaded, d => d.Category == "CEO技能" && d.Name == "丢牛鲨睾酮");
        foreach (var d in loaded)
        {
            MacroScript.Parse(d.Script);
        }
    }

    [Fact]
    public void SanitizesFileNames() => Assert.Equal("a_b_c", CustomMacroDefinition.SanitizeFileName("a/b:c"));
}

public class SteamLinkTests
{
    [Fact]
    public void ParsesBotJson()
    {
        const string json = "{\"链接\":\"steam://rungame/3240220/76561190000000000/-steamjvp=QUJDRA==\",\"昵称\":\"测试BOT\",\"获取时间\":\"2026-01-02 03:04:05\"}";
        var link = SteamJoinLink.FromJson(json);
        Assert.NotNull(link);
        Assert.True(link!.IsValid);
        Assert.Equal("3240220", link.AppId);
        Assert.Equal("76561190000000000", link.SteamId);
        Assert.Equal("增强版战局", link.EditionName);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), link.ObtainedAt);
        Assert.Equal("测试BOT", link.Nickname);
        Assert.Null(SteamJoinLink.FromJson(""));
    }

    [Fact]
    public void FindsLatestLinkInConsoleLog()
    {
        var log = """
            [2026-10-01 10:00:00] ExecuteSteamURL: "steam://rungame/3240220/76561199000000001/-steamjvp%3DAAAA%3D%3D"
            [2026-10-02 11:00:00] something else
            [2026-10-03 12:30:15] ExecuteSteamURL: "steam://rungame/271590/76561199000000002/-steamjvp%3DBBBB%3D%3D"
            """;
        var link = SteamJoinLink.LatestFromConsoleLog(log, "bot");
        Assert.NotNull(link);
        Assert.Equal("steam://rungame/271590/76561199000000002/-steamjvp=BBBB==", link!.Url);
        Assert.Equal("传承版战局", link.EditionName);
        Assert.Equal(new DateTime(2026, 10, 3, 12, 30, 15), link.ObtainedAt);
    }

    [Fact]
    public void JsonRoundTripKeepsChinese()
    {
        var link = new SteamJoinLink("steam://rungame/3240220/1/-steamjvp=X==", "昵称", new DateTime(2026, 1, 2, 3, 4, 5));
        var json = link.ToJson();
        Assert.Contains("\"链接\"", json);
        Assert.Contains("昵称", json);
        Assert.Equal(link, SteamJoinLink.FromJson(json));
    }

    [Fact]
    public void ClipboardText()
    {
        Assert.NotNull(SteamJoinLink.FromText("加入 steam://rungame/3240220/7/-steamjvp=abc== 吧", DateTime.Now));
        Assert.Null(SteamJoinLink.FromText("no link here", DateTime.Now));
    }
}

public class NetworkTests
{
    [Fact]
    public void StartupMetaEmbedsAndReadsPassword()
    {
        var enhanced = SessionLockFile.Build("abc123", GtaEdition.Enhanced);
        Assert.Contains("scaleform_frontend_gen9.rpf", enhanced);
        Assert.Equal("abc123", SessionLockFile.ReadPassword(enhanced));
        var legacy = SessionLockFile.Build("密码", GtaEdition.Legacy);
        Assert.DoesNotContain("gen9", legacy);
        Assert.Equal("密码", SessionLockFile.ReadPassword(legacy));
        Assert.Throws<ArgumentException>(() => SessionLockFile.Build(" ", GtaEdition.Legacy));
    }

    [Fact]
    public void WriteReadRemoveInGameFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        SessionLockFile.Write(folder, "pw", GtaEdition.Enhanced);
        Assert.Equal("pw", SessionLockFile.Read(folder));
        Assert.True(SessionLockFile.Remove(folder));
        Assert.Null(SessionLockFile.Read(folder));
        Assert.False(SessionLockFile.Remove(folder));
    }

    [Fact]
    public void RandomPasswordLength()
    {
        Assert.Equal(10, SessionLockFile.RandomPassword(10).Length);
        Assert.Equal(100, SessionLockFile.RandomPassword(500).Length);
    }

    [Fact]
    public void RuleSpecs()
    {
        Assert.Equal("80,443", NetworkRules.ForCut("仅断开存档(80,443)").RemotePorts);
        Assert.Contains("cs-gta5-prod.ros.rockstargames.com", NetworkRules.ForCut("仅断开云存档").RemoteHosts);
        Assert.Contains("185.56.65.200", NetworkRules.ForCut("仅断开交易").RemoteAddresses);
        Assert.Empty(NetworkRules.ForCut("全部断网").RemoteHosts);
        Assert.Equal((RuleProtocol.Udp, "6672"), (NetworkRules.SessionLock.Protocol, NetworkRules.SessionLock.RemotePorts));
        Assert.Equal(8_000_000UL, NetworkRules.ThrottleBitsPerSecond(1000));
    }
}

public class TimingTests
{
    private sealed class FakeFps(double fps) : IFrameRateSource
    {
        public double CurrentFps => fps;

        public double RecentMinimumFps => fps;
    }

    [Fact]
    public void TimerFormatSplitsSuffix()
    {
        var (main, suffix) = TimerFormat.Format(new TimeSpan(0, 1, 2, 3, 45), "hh:mm:ss|:iii");
        Assert.Equal("01:02:03", main);
        Assert.Equal(":045", suffix);
        Assert.Equal("02分03秒", TimerFormat.FormatFlat(new TimeSpan(0, 0, 2, 3), "mm分ss秒"));
    }

    [Fact]
    public void AdaptiveDelaySemantics()
    {
        var delay = new DelayProvider(() => new DelayOptions(false, true, 2), new FakeFps(50));
        Assert.Equal(22, delay.Resolve(10));
        Assert.Equal(40, delay.Resolve(40));
        Assert.Equal(0, delay.Resolve(0));
        Assert.Equal(20, delay.Resolve(-20));
        var plain = new DelayProvider(() => new DelayOptions(false, false, 0), new FakeFps(50));
        Assert.Equal(10, plain.Resolve(10));
    }

    [Fact]
    public void WaitHonoursCancellation()
    {
        var delay = new DelayProvider(() => new DelayOptions(true, false, 0));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => delay.Wait(1000, cts.Token));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        delay.Wait(20, CancellationToken.None);
        Assert.InRange(sw.ElapsedMilliseconds, 15, 500);
    }

    [Fact]
    public void ClickClassifierGestures()
    {
        var c = new ClickClassifier(200, 500);
        c.Down(0);
        Assert.Empty(c.Up(50));
        Assert.Empty(c.Poll(100));
        Assert.Equal(new[] { PressGesture.Click }, c.Poll(260));

        c.Down(1000);
        c.Up(1050);
        c.Down(1100);
        Assert.Equal(new[] { PressGesture.DoubleClick }, c.Up(1150));
        Assert.Empty(c.Poll(2000));

        c.Down(3000);
        Assert.Empty(c.Poll(3200));
        Assert.Equal(new[] { PressGesture.LongPress }, c.Poll(3600));
        Assert.Empty(c.Up(3700));
        Assert.Empty(c.Poll(5000));
    }
}
