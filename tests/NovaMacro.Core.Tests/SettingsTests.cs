using System.Text;
using NovaMacro.Core.Features;
using NovaMacro.Core.Ini;
using NovaMacro.Core.Input;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Tests;

public class IniTests
{
    [Fact]
    public void ParsesSectionsAndPreservesOrderAndValues()
    {
        var doc = IniDocument.Parse("[A]\r\nx=1\r\ny= spaced value\r\n[B]\r\nz={\"k\":\"v=w\"}\r\n");
        Assert.Equal("1", doc.Get("A", "x"));
        Assert.Equal(" spaced value", doc.Get("A", "y"));
        Assert.Equal("{\"k\":\"v=w\"}", doc.Get("B", "z"));
        Assert.Equal(new[] { "A", "B" }, doc.Sections.Select(s => s.Name));
    }

    [Fact]
    public void RoundTripsThroughText()
    {
        var doc = IniDocument.Parse("[武器卡弹设置]\n按键延迟=25\n[其他设置]\n退出方式=最小化到托盘\n");
        doc.Set("其他设置", "新键", "值");
        var again = IniDocument.Parse(doc.ToString());
        Assert.Equal("25", again.Get("武器卡弹设置", "按键延迟"));
        Assert.Equal("值", again.Get("其他设置", "新键"));
    }

    [Fact]
    public void DecodesLegacyGbkSettingsFile()
    {
        var text = "[武器卡弹设置]\r\n宏模式=自动速射\r\n";
        var bytes = IniEncoding.LegacyEncoding.GetBytes(text);
        Assert.False(IniEncoding.IsValidUtf8(bytes));
        Assert.Equal(text, IniEncoding.Decode(bytes));
    }

    [Fact]
    public void DecodesUtf8WithAndWithoutBom()
    {
        var text = "[小工具配置]\n联动IP=127.0.0.1:19730\n";
        Assert.Equal(text, IniEncoding.Decode(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, IniEncoding.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
    }

    [Fact]
    public void SaveWritesUtf8BomAndReloads()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Settings.ini");
        var doc = IniDocument.Parse("[一]\n二=三\n");
        doc.Save(path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Equal("三", IniDocument.Load(path).Get("一", "二"));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void BundledDefaultsAreTheUsersSettings()
    {
        var store = new SettingsStore(new IniDocument());
        Assert.Equal(700, store.GetInt(Sec.WeaponJam, "懒人速射间隔"));
        Assert.Equal("自动速射", store.GetString(Sec.WeaponJam, "宏模式"));
        Assert.Equal("重型武器", store.GetString(Sec.WeaponJam, "欲切枪武器键"));
        Assert.True(store.GetBool(Sec.WeaponJam, "启用狙击步枪"));
        Assert.Equal(8, store.GetInt(Sec.Tools, "游戏卡单时间"));
        Assert.Equal(1000, store.GetInt(Sec.Tools, "游戏限速速率"));
        Assert.Equal("127.0.0.1:19730", store.GetString(Sec.Tools, "联动IP"));
        Assert.Equal(40, store.GetInt(Sec.MapTeleport, "读秒法等待"));
        Assert.Equal(15879332, store.GetInt(Sec.Cayo, "紫色标点颜色"));
        Assert.Equal(700, store.GetInt(Sec.Phone, "手机_呼出后延迟"));
        Assert.Equal(1500, store.GetInt(Sec.DelayClimb, "自动打开相机时间"));
        Assert.Equal("hh:mm:ss|:iii", store.GetString(Sec.Hud, "计时器时间格式"));
    }

    [Fact]
    public void BundledDefaultsContainNoPersonalData()
    {
        var defaults = SettingsStore.LoadBundledDefaults().ToString();
        foreach (var forbidden in new[] { "steam://", "Steam_一号BOT", "Steam_二号BOT", "联动昵称", "游戏文件夹路径", "窗口位置记忆", "主界面=", "已使用", "RegistryBackup", @":\" })
        {
            Assert.DoesNotContain(forbidden, defaults);
        }

        var store = new SettingsStore(new IniDocument());
        Assert.Equal("玩家", store.GetString(Sec.Tools, "联动昵称"));
        Assert.Equal(string.Empty, store.GetString(Sec.Device, "游戏文件夹路径"));
        Assert.Equal(string.Empty, store.GetString(Sec.MapTeleport, "Steam_一号BOT"));
    }

    [Fact]
    public void UserSwitchesMapToFeatures()
    {
        var store = new SettingsStore(new IniDocument());
        Assert.True(store.IsFeatureOn(FeatureCatalog.Get(FeatureId.WeaponJam)));
        Assert.True(store.IsFeatureOn(FeatureCatalog.Get(FeatureId.BuyAmmo)));
        Assert.True(store.IsFeatureOn(FeatureCatalog.Get(FeatureId.ServiceVehicle)));
        Assert.True(store.IsFeatureOn(FeatureCatalog.Get(FeatureId.Afk)));
        Assert.False(store.IsFeatureOn(FeatureCatalog.Get(FeatureId.QuickPerspective)));
        Assert.False(store.IsFeatureOn(FeatureCatalog.Get(FeatureId.MapTeleport)));
        Assert.True(store.GetBool(Sec.Switches, "启用同步退出"));
    }

    [Fact]
    public void MapTeleportHotkeyIsF7FromUserFile()
    {
        var store = new SettingsStore(new IniDocument());
        var hk = store.GetHotkey(FeatureCatalog.Get(FeatureId.MapTeleport));
        Assert.Equal(InputCodes.Function(7), hk.Key);
        Assert.Equal(ModifierMask.None, hk.Modifiers);
    }

    [Fact]
    public void FileValuesOverrideDefaultsAndSchemaClamps()
    {
        var store = new SettingsStore(IniDocument.Parse("[武器卡弹设置]\n懒人速射间隔=650\n按键延迟=99999\n"));
        Assert.Equal(650, store.GetInt(Sec.WeaponJam, "懒人速射间隔"));
        Assert.Equal(2000, store.GetInt(Sec.WeaponJam, "按键延迟"));
    }

    [Fact]
    public void SchemaDefaultsFillMissingKeys()
    {
        var store = new SettingsStore(new IniDocument(), defaults: new IniDocument());
        Assert.Equal(30, store.GetInt(Sec.MenuLayout, "最低按键延迟"));
        Assert.Equal("防火墙规则断网", store.GetString(Sec.Tools, "游戏断网方法"));
        Assert.Equal(InputCodes.CapsLock, store.GetInt(Sec.Other, "辅助触发键"));
    }

    [Fact]
    public void HotkeyRoundTripAndChangeEvent()
    {
        var store = new SettingsStore(new IniDocument());
        var feature = FeatureCatalog.Get(FeatureId.BuyAmmo);
        var raised = 0;
        store.Changed += (_, _) => raised++;
        store.SetHotkey(feature, new Hotkey(InputCodes.Letter('Q'), ModifierMask.Ctrl | ModifierMask.Shift, InputCodes.CapsLock));
        var back = store.GetHotkey(feature);
        Assert.Equal(new Hotkey(InputCodes.Letter('Q'), ModifierMask.Ctrl | ModifierMask.Shift, InputCodes.CapsLock), back);
        Assert.True(raised >= 1);
    }

    [Fact]
    public void CachedValuesAreInvalidatedOnWrite()
    {
        var store = new SettingsStore(new IniDocument());
        Assert.Equal(700, store.GetInt(Sec.WeaponJam, "懒人速射间隔"));
        store.SetInt(Sec.WeaponJam, "懒人速射间隔", 640);
        Assert.Equal(640, store.GetInt(Sec.WeaponJam, "懒人速射间隔"));
        store.Remove(Sec.WeaponJam, "懒人速射间隔");
        Assert.Equal(700, store.GetInt(Sec.WeaponJam, "懒人速射间隔"));
        store.Set(Sec.WeaponJam, "宏模式", "单次切枪");
        store.ResetSection(Sec.WeaponJam);
        Assert.Equal("自动速射", store.GetString(Sec.WeaponJam, "宏模式"));
    }

    [Fact]
    public void ConcurrentReadsDuringWritesAreSafe()
    {
        var store = new SettingsStore(new IniDocument());
        var stop = false;
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                _ = store.GetInt(Sec.WeaponJam, "按键延迟");
                _ = store.GetBool(Sec.Switches, "武器卡弹");
            }
        });
        for (var i = 0; i < 2000; i++)
        {
            store.SetInt(Sec.WeaponJam, "按键延迟", 20 + (i % 5));
        }

        Volatile.Write(ref stop, true);
        reader.Wait();
        Assert.Equal(24, store.GetInt(Sec.WeaponJam, "按键延迟"));
    }

    [Fact]
    public void BooleansUseChineseTrueFalse()
    {
        Assert.True(SettingValues.ToBool("真"));
        Assert.False(SettingValues.ToBool("假", true));
        Assert.Equal("真", SettingValues.FromBool(true));
    }

    [Fact]
    public void EveryChoiceDefaultIsOneOfItsChoices()
    {
        foreach (var d in SettingsSchema.All.Where(d => d.Kind == SettingKind.Choice))
        {
            Assert.Contains(d.Default, d.Choices!);
        }
    }

    [Fact]
    public void SchemaHasNoDuplicateKeys()
    {
        var dupes = SettingsSchema.All.GroupBy(d => (d.Section, d.Key)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void OpenCreatesFileFromDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Settings.ini");
        var store = SettingsStore.Open(path);
        Assert.True(File.Exists(path));
        Assert.Equal("自动速射", store.GetString(Sec.WeaponJam, "宏模式"));
    }
}

public class KeyNameTests
{
    [Theory]
    [InlineData("A键", 0x41)]
    [InlineData("F7键", 118)]
    [InlineData("左光标键", 0x25)]
    [InlineData("侧键下", InputCodes.XButton1)]
    [InlineData("滚轮向上", InputCodes.WheelUp)]
    [InlineData("XB_A", InputCodes.PadA)]
    [InlineData("大写键", InputCodes.CapsLock)]
    [InlineData("Enter", InputCodes.Enter)]
    public void ParsesNames(string name, int code)
    {
        Assert.True(KeyNames.TryParse(name, out var parsed));
        Assert.Equal(code, parsed);
    }

    [Fact]
    public void EveryCodeNameParsesBack()
    {
        foreach (var code in KeyNames.AllCodes)
        {
            Assert.True(KeyNames.TryParse(KeyNames.GetName(code), out var back), KeyNames.GetName(code));
            Assert.Equal(code, back);
        }
    }

    [Fact]
    public void HotkeyDisplay()
    {
        Assert.Equal("Alt键 + R键", new Hotkey(InputCodes.Letter('R'), ModifierMask.Alt).ToString());
        Assert.Equal("未设置", Hotkey.None.ToString());
    }
}
