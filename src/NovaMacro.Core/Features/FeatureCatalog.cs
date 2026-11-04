using NovaMacro.Core.Input;

namespace NovaMacro.Core.Features;

public enum FeatureId
{
    WeaponJam,
    BuyAmmo,
    MenuSnack,
    VehicleControl,
    ServiceVehicle,
    QuickPerspective,
    PhoneCall,
    AutoClick,
    WheelSnack,
    WeaponSwapSpeed,
    AdSpeed,
    InstantHover,
    SwitchSession,
    SprintToggle,
    DelayClimb,
    EnterAlternative,
    Afk,
    CasinoHeist,
    CayoPerico,
    MapTeleport,
    JobTeleport,
    Screenshot,
    Crosshair,
    KillGame,
    NetworkCut,
    SoloSession,
    SessionLock,
    SpeedLimit,
    StopAll,
    CustomMacro,
}

public enum FeatureGroup
{
    Macro,
    Tool,
    System,
}

/// <param name="Switch">Key in [记忆开关] holding the on/off state (null = always on).</param>
/// <param name="HotkeySection">INI section ("…热键") storing 键/功能键/副键 (null = no configurable hotkey).</param>
public sealed record FeatureDefinition(
    FeatureId Id,
    string Name,
    FeatureGroup Group,
    string? Switch,
    bool DefaultOn,
    string? HotkeySection,
    Hotkey DefaultHotkey,
    string Help,
    string? SettingsSection = null,
    string? HotkeyKey = null);

public static class FeatureCatalog
{
    public static IReadOnlyList<FeatureDefinition> All { get; } = new FeatureDefinition[]
    {
        new(FeatureId.WeaponJam, "武器卡弹", FeatureGroup.Macro, "武器卡弹", true, "武器卡弹热键", new Hotkey(InputCodes.XButton1),
            "按住热键自动开火并切枪卡弹，实现重型武器/狙击枪速射\n按住【切换欲切枪武器功能键】再按热键轮换欲切枪武器", "武器卡弹设置"),
        new(FeatureId.BuyAmmo, "买满子弹", FeatureGroup.Macro, "买满子弹", true, "买满子弹热键", new Hotkey(InputCodes.Letter('R'), ModifierMask.Alt),
            "单击热键在空手或手持手枪、突击步枪可买满所有子弹\n按住特殊触发键并触发热键则可购买当前武器子弹", "菜单零食设置"),
        new(FeatureId.MenuSnack, "菜单零食", FeatureGroup.Macro, "菜单零食", false, "列表零食热键", new Hotkey(InputCodes.Letter('F'), ModifierMask.Alt),
            "单击热键吃设定个数的零食（个数为0时按住持续吃）\n按住特殊触发键并触发热键则穿戴防弹衣", "菜单零食设置"),
        new(FeatureId.VehicleControl, "载具启动", FeatureGroup.Macro, "载具启动", false, "载具启动热键", new Hotkey(InputCodes.Letter('V'), ModifierMask.Alt),
            "单击热键执行设定的载具操作（默认控制引擎）\n按住辅助触发键（如大写键）单击热键可把载具收回，双击则叫出", "菜单零食设置"),
        new(FeatureId.ServiceVehicle, "呼叫载具", FeatureGroup.Macro, "服务载具", true, "呼叫载具热键", new Hotkey(InputCodes.Letter('B'), ModifierMask.Alt),
            "单击呼叫，双击收回，也可在设置调整轮换的服务载具\n按住辅助触发键（如大写键）单击热键来轮换载具", "菜单零食设置"),
        new(FeatureId.QuickPerspective, "快速人称", FeatureGroup.Macro, "快速人称", false, "快速人称热键", new Hotkey(InputCodes.Letter('V'), ModifierMask.Ctrl),
            "快速在第三人称与第一人称之间切换，请注意设置相关参数", "快速人称设置"),
        new(FeatureId.PhoneCall, "电话呼叫", FeatureGroup.Macro, "电话呼叫", false, "电话呼叫热键", new Hotkey(InputCodes.Letter('P'), ModifierMask.Alt),
            "按住热键后按数字键选择联系人（松开默认呼叫第一个）", "电话呼叫设置"),
        new(FeatureId.AutoClick, "连续左键", FeatureGroup.Macro, "连续左键", false, null, Hotkey.None,
            "按住特殊触发键（如大写键）并双击鼠标左键即可连续点击鼠标左键，松开大写键则停止\n若间隔时间设为负数，则启动后无需按住特殊触发键，再次按下鼠标左键或特殊触发键即可停止", "连续左键设置"),
        new(FeatureId.WheelSnack, "快速零食", FeatureGroup.Macro, "快速零食", false, null, Hotkey.None,
            "可在武器轮盘里进行快速吃零食\n必须打开Tab轮盘，之后可按住读取的零食键或设置的侧键", "其他设置"),
        new(FeatureId.WeaponSwapSpeed, "切枪偷速", FeatureGroup.Macro, "切枪偷速", false, null, Hotkey.None,
            "按下设定的组合键开启循环切枪，松开设定的键则结束循环\n期间可以选择按住鼠标右键的操作", "其他设置"),
        new(FeatureId.AdSpeed, "AD偷速", FeatureGroup.Macro, "AD偷速", false, null, Hotkey.None,
            "按住Shift + W + E 即可开始AD偷速跑\n（如在室内限制速度时偷速或打开武器轮盘边自动吃零食边跑）"),
        new(FeatureId.InstantHover, "瞬间悬停", FeatureGroup.Macro, "瞬间悬停", false, null, Hotkey.None,
            "在按住特殊触发键 + [角色选择器/切换到麦克/切换到富兰克林/切换到崔佛]游戏键位\n将会自动进行悬停操作", "瞬间悬停设置"),
        new(FeatureId.SwitchSession, "切换战局", FeatureGroup.Macro, "切换战局", false, null, Hotkey.None,
            "按住暂停菜单键（或 XB_Menu）+ 1/2/3 或 ←/↓/→ 将会自动进行切换战局操作\n【1、←：公开；2、↓：邀请；3、→：好友】"),
        new(FeatureId.SprintToggle, "奔跑切换", FeatureGroup.Macro, "奔跑切换", false, "奔跑切换热键", new Hotkey(InputCodes.Letter('X'), ModifierMask.Alt),
            "按下热键来切换奔跑的状态（即按住或不按住）\n若主动长按超过一秒则视为不切换"),
        new(FeatureId.DelayClimb, "延迟攀爬", FeatureGroup.Macro, "延迟攀爬", false, "延迟攀爬热键", new Hotkey(InputCodes.Letter('G'), ModifierMask.Alt),
            "触发后自动切空手并在设定时间后打开相机（延迟攀爬穿墙）", "延迟攀爬设置"),
        new(FeatureId.EnterAlternative, "替代回车", FeatureGroup.Macro, "替代回车", true, null, Hotkey.None,
            "Ctrl + 空格即可替代回车，也可在其他设置界面启用大写键 + 空格 / 大写键 + 左键", "其他设置"),
        new(FeatureId.Afk, "挂机", FeatureGroup.Macro, "挂机", true, "挂机热键", new Hotkey(InputCodes.Function(9), ModifierMask.Ctrl),
            "开启后按设定间隔点击/按住挂机键位防止被踢出战局\n空闲挂机：游戏在前台且超过设定时间无操作时自动按一次挂机键", "其他设置"),
        new(FeatureId.CasinoHeist, "赌场豪劫", FeatureGroup.Macro, "赌场豪劫", false, "赌场豪劫热键", new Hotkey(InputCodes.Function(9)),
            "在指纹破解界面触发热键来自动进行指纹匹配\n在其他界面触发则显示捕获的门禁密码", "赌场豪劫设置"),
        new(FeatureId.CayoPerico, "佩里科岛", FeatureGroup.Macro, "佩里科岛", false, "佩里科岛热键", new Hotkey(InputCodes.Function(11)),
            "在指纹破解界面触发热键来自动进行指纹匹配\n在飞机上（地图缩小状态并有紫色标点）触发则开始自动梅杜莎飞行，可再次按住热键来紧急停止", "佩里科岛设置"),
        new(FeatureId.MapTeleport, "地图差传", FeatureGroup.Macro, "地图差传", false, "地图差传热键", new Hotkey(InputCodes.Function(7)),
            "差传方法：BOT法 / 听声法 / 读秒法\nBOT法：通过 Steam 战局链接或好友列表加入 BOT 战局完成差传", "地图差传设置"),
        new(FeatureId.JobTeleport, "任务卡传", FeatureGroup.Macro, "任务卡传", false, "任务卡传热键", new Hotkey(InputCodes.Letter('T'), ModifierMask.Alt),
            "快速加入随机差事并判断匹配差事，期间再次触发 M 菜单键跳过判断\n按住特殊触发键可直接注册对应的老大（即跳过快速加入差事）", "任务卡传设置"),
        new(FeatureId.Screenshot, "内置截图", FeatureGroup.Macro, "内置截图", false, "内置截图热键", new Hotkey(InputCodes.Function(12), ModifierMask.Ctrl),
            "完成截图后会置入粘贴板（默认返回全屏截图），只在游戏内可触发"),
        new(FeatureId.Crosshair, "外置准星", FeatureGroup.Macro, "外置准星", false, "外置准星热键", new Hotkey(InputCodes.Letter('Z'), ModifierMask.Ctrl),
            "Ctrl + Z热键显示/隐藏准星\n游戏界面改变则重新开启准星以改变对应大小和位置", "外置准星设置"),

        new(FeatureId.KillGame, "结束进程", FeatureGroup.Tool, null, true, "小工具配置", new Hotkey(InputCodes.Function(5), ModifierMask.Ctrl), "强制结束游戏进程", "小工具配置", "结束进程热键"),
        new(FeatureId.NetworkCut, "游戏断网", FeatureGroup.Tool, null, true, "小工具配置", new Hotkey(InputCodes.Function(6), ModifierMask.Ctrl), "对游戏断网/恢复网络", "小工具配置", "游戏断网热键"),
        new(FeatureId.SoloSession, "游戏卡单", FeatureGroup.Tool, null, true, "小工具配置", new Hotkey(InputCodes.Function(7), ModifierMask.Ctrl), "暂停游戏数秒以卡出单人战局", "小工具配置", "游戏卡单热键"),
        new(FeatureId.SessionLock, "战局锁单", FeatureGroup.Tool, null, true, "小工具配置", new Hotkey(InputCodes.Function(8), ModifierMask.Ctrl),
            "规则仅阻止玩家链接！注意：锁单期间你无法Home差传", "小工具配置", "战局锁单热键"),
        new(FeatureId.SpeedLimit, "游戏限速", FeatureGroup.Tool, null, true, "小工具配置", new Hotkey(InputCodes.Function(9), ModifierMask.Ctrl | ModifierMask.Shift), "使用系统 QoS 策略限制游戏网速", "小工具配置", "游戏限速热键"),

        new(FeatureId.StopAll, "一键停止", FeatureGroup.System, null, true, "一键停止宏热键", new Hotkey(InputCodes.Pause),
            "单击停止按键宏操作，长按锁定/解锁按键宏"),
    };

    public static FeatureDefinition Get(FeatureId id) => All.First(f => f.Id == id);

    public static FeatureDefinition? FindByName(string name) => All.FirstOrDefault(f => f.Name == name || f.Switch == name);
}
