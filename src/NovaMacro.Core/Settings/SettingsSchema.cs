using NovaMacro.Core.Input;

namespace NovaMacro.Core.Settings;

/// <summary>Section names, identical to the original tool's Settings.ini.</summary>
public static class Sec
{
    public const string WeaponJam = "武器卡弹设置";
    public const string Device = "此设备已使用";
    public const string WindowPositions = "窗口位置记忆";
    public const string Other = "其他设置";
    public const string Switches = "记忆开关";
    public const string Tools = "小工具配置";
    public const string Hud = "HUD辅助设置";
    public const string MapTeleport = "地图差传设置";
    public const string MenuSnack = "菜单零食设置";
    public const string DelayClimb = "延迟攀爬设置";
    public const string QuickPerspective = "快速人称设置";
    public const string Casino = "赌场豪劫设置";
    public const string Cayo = "佩里科岛设置";
    public const string Phone = "手机延迟设置";
    public const string PhoneCall = "电话呼叫设置";
    public const string JobTeleport = "任务卡传设置";
    public const string Hover = "瞬间悬停设置";
    public const string Crosshair = "外置准星设置";
    public const string AutoClick = "连续左键设置";
    public const string StopMacro = "一键停止宏热键";
    public const string Debug = "调试窗口设置";
    public const string KeyOverrides = "游戏键位覆盖";
    public const string MenuLayout = "M菜单布局";
}

public static class SettingsSchema
{
    private static readonly string[] Weapons = { "重型武器", "狙击步枪", "突击步枪", "手枪", "机枪", "霰弹枪" };
    private static readonly string[] MacroModes = { "默认模式", "自动速射", "单次切枪", "开火激活" };

    public static IReadOnlyList<string> WeaponNames => Weapons;

    public static IReadOnlyList<string> ServiceVehicles { get; } = new[] { "个人载具", "机动作战中心", "复仇者", "恐霸", "虎鲸", "致幻剂实验室" };

    public static IReadOnlyList<string> ArmorTypes { get; } = new[] { "自动识别", "超轻型防弹衣", "轻型防弹衣", "标准防弹衣", "重型防弹衣", "超重型防弹衣" };

    public static IReadOnlyList<SettingDescriptor> All { get; } = Build();

    public static IEnumerable<string> SectionOrder => All.Select(d => d.Section).Distinct();

    private static readonly Dictionary<(string, string), SettingDescriptor> Index =
        All.GroupBy(d => (d.Section, d.Key)).ToDictionary(g => g.Key, g => g.First());

    public static SettingDescriptor? Find(string section, string key) =>
        Index.TryGetValue((section, key), out var d) ? d : null;

    private static List<SettingDescriptor> Build()
    {
        var l = new List<SettingDescriptor>();

        void B(string s, string k, bool def, string label, string? help = null, bool hidden = false) =>
            l.Add(new(s, k, SettingKind.Bool, SettingValues.FromBool(def), label, help, Hidden: hidden));
        void I(string s, string k, int def, string label, string? help = null, int min = int.MinValue, int max = int.MaxValue, bool hidden = false) =>
            l.Add(new(s, k, SettingKind.Int, def.ToString(), label, help, Min: min, Max: max, Hidden: hidden));
        void T(string s, string k, string def, string label, string? help = null, bool hidden = false) =>
            l.Add(new(s, k, SettingKind.Text, def, label, help, Hidden: hidden));
        void C(string s, string k, string def, string label, IReadOnlyList<string> choices, string? help = null) =>
            l.Add(new(s, k, SettingKind.Choice, def, label, help, choices));
        void K(string s, string k, int def, string label, string? help = null) =>
            l.Add(new(s, k, SettingKind.Key, def.ToString(), label, help));
        void Col(string s, string k, int def, string label, string? help = null) =>
            l.Add(new(s, k, SettingKind.Color, def.ToString(), label, help));
        void M(string s, string k, int def, string label, string? help = null) =>
            l.Add(new(s, k, SettingKind.Modifier, def.ToString(), label, help));

        // ---------------- 武器卡弹 ----------------
        var w = Sec.WeaponJam;
        I(w, "按键延迟", 25, "按键延迟(毫秒)", "每次按键之后的等待时间", 1, 2000);
        I(w, "按键间隔", 25, "按键间隔(毫秒)", "按键按下与弹起之间的间隔", 1, 2000);
        C(w, "宏模式", "自动速射", "宏模式", MacroModes,
            "默认模式：开火后立即切枪卡弹一次\n自动速射：按住热键持续开火+切枪卡弹\n单次切枪：只做一次切枪动作\n开火激活：按住热键时，每次按下开火键自动卡弹");
        B(w, "锁定宏模式", false, "锁定宏模式", "锁定后无法用热键切换宏模式");
        I(w, "懒人速射间隔", 700, "自动速射间隔(默认)", "自动速射模式下两次开火之间的间隔(毫秒)", 0, 10000);
        C(w, "欲切枪武器键", "重型武器", "欲切枪武器", Weapons, "欲切枪的武器，可点击弹出下拉框选择");
        foreach (var weapon in Weapons)
        {
            B(w, "启用" + weapon, weapon is "重型武器" or "狙击步枪", "启用" + weapon, "关闭后热键切换欲切枪武器时跳过该武器");
            I(w, "自动速射间隔_" + (weapon == "狙击步枪" ? "狙击枪" : weapon), 0, weapon + "速射间隔", "0 表示使用默认间隔", 0, 10000);
            C(w, "宏模式_" + weapon, "默认模式", weapon + "宏模式", MacroModes, "宏模式独立设置开启后生效");
        }

        B(w, "宏模式独立设置", false, "宏模式独立设置", "为每种武器单独设置宏模式");
        C(w, "自动速射开火顺序", "先开火后切枪", "自动速射开火顺序", new[] { "先开火后切枪", "先切枪后开火" });
        C(w, "宏结束操作", "仅第一发完整", "宏结束操作", new[] { "全部提前结束", "仅第一发完整", "全部操作完整" }, "松开热键时如何结束当前速射");
        B(w, "武器轮盘式切换", false, "武器轮盘式切换", "通过 Tab 武器轮盘切换武器（键位切换不可用时）");
        B(w, "武器键位切换", true, "武器键位切换", "通过游戏武器栏位键切换武器");
        B(w, "隐藏切枪提示", false, "隐藏切枪提示");
        B(w, "快速收起武器轮盘", true, "快速收起武器轮盘");
        M(w, "切换欲切枪武器热键功能键", 0, "切换欲切枪武器功能键", "按住该功能键再按武器卡弹热键可轮换欲切枪武器（0=禁用）");
        K(w, "投掷武器键位", InputCodes.Digit(5), "投掷武器键位", "卡弹时临时切换到的武器栏位");

        // ---------------- 菜单零食 / M菜单宏 ----------------
        var m = Sec.MenuSnack;
        I(m, "按键延迟", 1, "M菜单按键延迟(毫秒)", "设置按键之后延时（最低1毫秒）", 1, 2000);
        I(m, "按键按下弹起间隔", 3, "M菜单按下弹起间隔(毫秒)", "设置按键的按下弹起间隔延时（最低1毫秒）", 1, 2000);
        I(m, "吃零食个数", 0, "吃零食个数", "0 = 按住热键持续吃零食", 0, 100);
        I(m, "吃零食间隔", 50, "吃零食间隔(毫秒)", "按住热键时的吃零食间隔", 1, 5000);
        C(m, "要穿的护甲", "自动识别", "要穿的护甲", ArmorTypes);
        B(m, "启用自适应买所有子弹", true, "启用自适应买所有子弹", "单击热键在空手或手持手枪、突击步枪可买满所有子弹\n按住特殊触发键并触发热键则可购买当前武器子弹");
        C(m, "M菜单选择模式", "方向键", "M菜单选择模式", new[] { "方向键", "鼠标滚轮" });
        C(m, "选择的服务载具", "虎鲸", "当前选择的服务载具", ServiceVehicles);
        T(m, "启用的服务载具", string.Join(",", ServiceVehicles), "启用的服务载具(逗号分隔)", "按住辅助触发键单击呼叫载具热键可在这些载具之间轮换");
        B(m, "载具启动停留引擎界面", false, "载具启动停留在控制引擎界面", "勾选以停留在控制引擎界面");
        C(m, "载具启动操作", "叫出/收回", "载具启动单击操作", new[] { "叫出/收回", "控制引擎", "控制车门", "控制全部" });

        // ---------------- M菜单布局 (可调整) ----------------
        var ml = Sec.MenuLayout;
        I(ml, "生命值与弹药位置", 4, "【生命值与弹药】在M菜单中的位置", "从 0 开始计数；负数表示倒数（如 -3 为倒数第3位）", -30, 30);
        I(ml, "弹药子菜单位置", 0, "【弹药】在生命值与弹药中的位置", null, 0, 10);
        I(ml, "防弹衣子菜单位置", 1, "【防弹衣】在生命值与弹药中的位置", null, 0, 10);
        I(ml, "零食子菜单位置", 2, "【零食】在生命值与弹药中的位置", null, 0, 10);
        I(ml, "个人载具位置", 3, "【个人载具】在M菜单中的位置", null, -30, 30);
        I(ml, "服务载具位置", 6, "【服务】(载具服务) 在M菜单中的位置", null, -30, 30);
        I(ml, "老大菜单位置", 0, "【SecuroServ/摩托帮】在M菜单中的位置", null, -30, 30);
        I(ml, "菜单打开延迟", 200, "打开菜单后的等待(毫秒)", null, 0, 3000);
        I(ml, "子菜单切换延迟", 100, "进入子菜单后的等待(毫秒)", null, 0, 3000);
        I(ml, "最低按键延迟", 30, "M菜单最低按键延迟(毫秒)", "游戏每帧才读取一次输入，过低的按键延迟会丢键；实际延迟取【按键延迟】与该值的较大者", 0, 1000);
        const string pathHelp = "路径语法：M / 暂停 / HOME / 手机 / ↑n ↓n ←n →n / 确认 / 返回 / 等待n / 滚上n 滚下n / 键:A键*2 / 动作:武器射击 / #标记\n{名称} 引用本页的位置数值，负数表示向上（倒数）";
        T(ml, "买满子弹路径", "M,↓{生命值与弹药位置},确认,↓{弹药子菜单位置},确认,←,↓,确认,M", "买满所有子弹路径", pathHelp);
        T(ml, "买当前子弹路径", "M,↓{生命值与弹药位置},确认,↓{弹药子菜单位置},确认,↓,确认,M", "买当前武器子弹路径", pathHelp);
        T(ml, "零食路径", "M,↓{生命值与弹药位置},确认,↓{零食子菜单位置},确认,#吃零食,M", "吃零食路径（#吃零食 处重复确认）", pathHelp);
        T(ml, "护甲路径", "M,↓{生命值与弹药位置},确认,↓{防弹衣子菜单位置},确认,↓{护甲},确认,M", "穿防弹衣路径（{护甲}=所选护甲序号）", pathHelp);
        T(ml, "叫出载具路径", "M,↓{个人载具位置},确认,↑2,确认,M", "叫出/收回个人载具路径", pathHelp);
        T(ml, "控制引擎路径", "M,↓{个人载具位置},确认,↑,确认,↑,确认,M", "控制引擎路径", pathHelp);
        T(ml, "控制车门路径", "M,↓{个人载具位置},确认,↑3,确认,确认,M", "控制车门路径", pathHelp);
        T(ml, "控制全部路径", "M,↓{个人载具位置},确认,↓2,→,确认,M", "控制全部路径", pathHelp);
        T(ml, "呼叫服务载具路径", "M,↓{服务载具位置},确认,↓{载具},确认,M", "呼叫服务载具路径（{载具}=载具序号）", pathHelp);
        T(ml, "送回服务载具路径", "M,↓{服务载具位置},确认,↓{载具},确认,M", "送回服务载具路径", pathHelp);
        T(ml, "呼叫内部载具路径", "M,↓{服务载具位置},确认,↓{载具},→,确认,M", "呼叫内部载具路径（长按热键）", pathHelp);
        T(ml, "注册CEO路径", "M,↓{老大菜单位置},确认,确认,确认", "注册 CEO 路径", pathHelp);
        T(ml, "注册摩托帮路径", "M,↓{老大菜单位置},确认,↓,确认,确认", "注册摩托帮路径", pathHelp);
        T(ml, "隐退路径", "M,↑,确认,确认", "隐退老大路径", pathHelp);
        T(ml, "快速加入差事路径", "暂停,等待1500,键:E键,等待300,确认,等待300,↓2,确认,等待300,↓{差事类型},确认", "快速加入随机差事路径", pathHelp);
        I(ml, "差事类型", 0, "快速加入差事类型序号", null, 0, 20);
        I(ml, "个人载具序号", 0, "个人载具在服务载具菜单的序号", null, 0, 20);
        I(ml, "机动作战中心序号", 0, "机动作战中心序号", null, 0, 20);
        I(ml, "复仇者序号", 1, "复仇者序号", null, 0, 20);
        I(ml, "恐霸序号", 2, "恐霸序号", null, 0, 20);
        I(ml, "虎鲸序号", 3, "虎鲸序号", null, 0, 20);
        I(ml, "致幻剂实验室序号", 4, "致幻剂实验室序号", null, 0, 20);
        T(ml, "公开战局路径", "等待1500,键:E键,等待300,确认,等待300,↓5,确认,等待300,确认,等待300,确认", "切换公开战局路径", pathHelp);
        T(ml, "邀请战局路径", "等待1500,键:E键,等待300,确认,等待300,↓5,确认,等待300,↓,确认,等待300,确认", "切换邀请战局路径", pathHelp);
        T(ml, "好友战局路径", "等待1500,键:E键,等待300,确认,等待300,↓5,确认,等待300,↓3,确认,等待300,确认", "切换好友战局路径", pathHelp);

        // ---------------- 快速人称 ----------------
        var qp = Sec.QuickPerspective;
        I(qp, "按键间隔", 50, "按键间隔(毫秒)", null, 1, 2000);
        I(qp, "按键延迟", 10, "按键延迟(毫秒)", null, 1, 2000);
        I(qp, "当前人称", 0, "当前人称(0=第一人称)", "软件记录的当前人称：0 第一人称，1 第三人称近，2 中，3 远", 0, 3);
        C(qp, "快速切换第三人称", "第三人称近", "快速切换的第三人称", new[] { "第三人称近", "第三人称中", "第三人称远" });

        // ---------------- 手机 / 电话 ----------------
        var ph = Sec.Phone;
        I(ph, "手机_按下弹起间隔", 50, "手机按下弹起间隔(毫秒)", null, 1, 2000);
        I(ph, "手机_呼出后延迟", 700, "呼出手机后延迟(毫秒)", null, 1, 5000);
        I(ph, "手机_选择后延迟", 70, "手机选择后延迟(毫秒)", null, 1, 5000);
        I(ph, "手机_滚动延迟", 50, "手机滚动延迟(毫秒)", null, 1, 2000);
        var pc = Sec.PhoneCall;
        T(pc, "电话位列", "Lester,-3;Mors Mutual Insurance,-7;Pegasus,-8;Mechanic,-9;Merryweather,-10", "电话联系人(名称,位置;…)",
            "联系人在通讯录中的位置；可设置为负数，如 -3 为倒数第3位，主要用于避免上方向键冲突导致呼出手机");
        I(pc, "通讯录位置", 2, "【联系人】图标在手机主屏的位置(0=左上，3列)", "从手机主屏左上角开始计数", 0, 14);
        I(pc, "相机位置", 6, "【相机】图标在手机主屏的位置(0=左上，3列)", null, 0, 14);
        I(pc, "选择等待时间", 600, "等待选择联系人(毫秒)", "请点击触发键或松开默认触发第一个", 100, 5000);

        // ---------------- 延迟攀爬 ----------------
        var dc = Sec.DelayClimb;
        I(dc, "自动打开相机时间", 1500, "自动打开相机时间(毫秒)", "毫秒后打开相机", 0, 10000);
        B(dc, "自动切空手", true, "自动切空手");
        B(dc, "二次预选相机", false, "二次预选相机");
        B(dc, "相机状态检测", false, "相机状态检测", "通过屏幕检测相机是否已关闭");
        B(dc, "自动相机", true, "自动相机");

        // ---------------- 瞬间悬停 ----------------
        B(Sec.Hover, "断网悬停", false, "断网悬停", "悬停时同时对游戏断网");
        I(Sec.Hover, "悬停持续时间", 1500, "悬停持续(毫秒)", null, 100, 20000);

        // ---------------- 赌场 / 佩岛 ----------------
        I(Sec.Casino, "赌场指纹按键间隔", 50, "指纹按键间隔(毫秒)", null, 1, 1000);
        I(Sec.Casino, "赌场指纹按键延迟", 50, "指纹按键延迟(毫秒)", null, 1, 1000);
        C(Sec.Casino, "处理方式", "内存处理", "截图处理方式", new[] { "内存处理", "文件处理" });
        I(Sec.Casino, "指纹匹配阈值", 14, "指纹最大汉明差异", "差异值超过该值视为匹配失败", 1, 64);
        I(Sec.Cayo, "佩岛指纹按键间隔", 50, "指纹按键间隔(毫秒)", null, 1, 1000);
        I(Sec.Cayo, "佩岛指纹按键延迟", 50, "指纹按键延迟(毫秒)", null, 1, 1000);
        Col(Sec.Cayo, "紫色标点颜色", 15879332, "紫色标点颜色", "小地图上主动标点的颜色");
        Col(Sec.Cayo, "飞机水平线颜色", 789516, "飞机水平线颜色", "飞机小地图水平线的颜色");
        I(Sec.Cayo, "颜色容差", 24, "颜色容差", null, 0, 128);
        I(Sec.Cayo, "梅杜莎最长时间", 180, "自动飞行最长时间(秒)", null, 10, 1200);
        T(Sec.Cayo, "指纹目标区域", "1340,330,545,780", "指纹目标区域(2560x1440 坐标 x,y,宽,高)");
        T(Sec.Cayo, "指纹拼图区域", "640,330,545,780", "指纹拼图区域(2560x1440 坐标 x,y,宽,高)");
        T(Sec.Cayo, "小地图区域", "53,1085,400,262", "小地图区域(2560x1440 坐标 x,y,宽,高)");
        I(Sec.Cayo, "指纹旋转等待", 120, "每次旋转后的等待(毫秒)", null, 10, 2000);

        // ---------------- 地图差传 ----------------
        var mt = Sec.MapTeleport;
        C(mt, "选择方法", "BOT法", "差传方法", new[] { "BOT法", "听声法", "读秒法" });
        I(mt, "读秒法等待", 40, "读秒法等待(秒)", null, 1, 600);
        C(mt, "差传BOT", "Steam差传", "差传BOT", new[] { "Steam差传", "Steam_一号BOT", "Steam_二号BOT" });
        B(mt, "Steam点击加入", false, "Steam点击加入", "在 Steam 好友列表中寻找玩家并点击加入（需要参考图片）");
        T(mt, "点击加入名称", string.Empty, "点击加入的用户名称");
        T(mt, "点击加入所在群组", string.Empty, "点击加入所在群组");
        T(mt, "Steam_一号BOT", string.Empty, "一号BOT战局链接(JSON)", hidden: true);
        T(mt, "Steam_二号BOT", string.Empty, "二号BOT战局链接(JSON)", hidden: true);
        B(mt, "隐藏提示", false, "隐藏提示", "勾选以隐藏错误提示，建议全屏游戏下勾选");
        B(mt, "静音其他进程", false, "听声法静音其他进程");
        B(mt, "翻找好友列表", false, "翻找好友列表", "BOT在好友列表下方因好友过多不可见时可勾选（测试阶段）");
        B(mt, "预载", false, "Home预载", "勾选后可在确定差传时双击HOME键预加载，3.5秒之后可更快载入好友列表");
        B(mt, "主动寻找", false, "主动寻找", "勾选后可在长时间未识别到好友位时主动按下鼠标左键来跳过");
        C(mt, "触发方式", "点击", "选择触发方式", new[] { "点击", "长按" }, "点击：检测到无法差传则直接加入BOT战局\n长按：按住热键后直接加入BOT战局");
        I(mt, "听声阈值", 12, "听声法音量阈值(%)", null, 1, 100);
        I(mt, "听声停顿", 900, "听声法停顿时间(毫秒)", null, 100, 10000);
        I(mt, "确认时限", 30, "确认阶段时限(秒)", null, 1, 120);

        // ---------------- 任务卡传 ----------------
        B(Sec.JobTeleport, "未购买服装厂", false, "未购买服装厂", "未购买服装厂时快速加入差事菜单位置不同");
        C(Sec.JobTeleport, "注册身份", "CEO", "卡传注册身份", new[] { "CEO", "摩托帮" });
        B(Sec.JobTeleport, "自动检测注册状态", true, "自动检测注册老大状态", "勾选后可自动检测注册老大状态来选择是否隐退");
        I(Sec.JobTeleport, "判断等待", 8000, "判断匹配差事等待(毫秒)", "等待匹配差事的时间，期间可按 M 菜单键跳过", 0, 120000);

        // ---------------- 外置准星 ----------------
        var ch = Sec.Crosshair;
        T(ch, "当前准星数据", "65280,89,1,8,3,0,16777215,0,0,0", "准星数据", "颜色,透明度,样式,长度,粗细,间隙,描边颜色,描边宽度,中心点,旋转", hidden: true);
        B(ch, "启用显示热键", true, "启用显示热键", "Ctrl + Z 热键显示/隐藏准星");
        B(ch, "嵌入窗口", false, "嵌入窗口", "将准星嵌入游戏窗口内，不会遮挡影响其他窗口（全屏模式下不可见）");
        B(ch, "像素对齐", true, "像素对齐");
        B(ch, "启用准星偏移", false, "启用准星偏移");
        T(ch, "准星中心偏移", "0|0", "准星中心偏移(x|y)");

        // ---------------- 其他设置 ----------------
        var o = Sec.Other;
        K(o, "辅助触发键", InputCodes.CapsLock, "辅助/特殊触发键", "按住该键配合热键触发辅助功能（默认大写键）");
        C(o, "退出方式", "最小化到托盘", "关闭主界面时", new[] { "最小化到托盘", "直接退出" }, "最小化到托盘：关闭主界面后继续在托盘运行，热键仍然有效（双击托盘图标重新打开）\n直接退出：关闭主界面即退出程序");
        C(o, "快速零食侧键", "按住键位触发", "快速零食触发", new[] { "按住键位触发", "侧键上", "侧键下" },
            "可在武器轮盘里进行快速吃零食\n必须打开Tab轮盘，之后可按住读取的零食键或设置的侧键");
        B(o, "快速零食穿护甲", false, "快速零食同时穿护甲");
        K(o, "挂机键位", InputCodes.Letter('C'), "挂机键位", "可设置按键，默认C键，不支持后台操作");
        C(o, "挂机方法", "点击", "挂机方法", new[] { "点击", "按住" });
        I(o, "挂机点击间隔", 2000, "挂机点击间隔(毫秒)", "在此设置大于0的挂机点击间隔时间", 1, 600000);
        B(o, "空闲挂机", false, "空闲挂机", "当游戏在前台时，每超过10分钟无任何操作时，将会执行一次设定的按键操作（仅点击操作）");
        I(o, "空闲挂机时间", 600, "空闲判定时间(秒)", null, 10, 7200);
        B(o, "回车_启用Ctrl空格", true, "Ctrl + 空格 替代回车");
        B(o, "回车_启用大写空格", false, "大写键 + 空格 替代回车");
        B(o, "回车_启用鼠标中键", false, "鼠标中键 替代回车");
        K(o, "切枪偷速键", InputCodes.XButton2, "切枪偷速键");
        M(o, "切枪偷速功能键", 0, "切枪偷速功能键");
        K(o, "切枪偷速副键", InputCodes.None, "切枪偷速副键");
        B(o, "切枪偷速瞄准操作", false, "切枪偷速瞄准续偷速");
        B(o, "切枪偷速松开停止", true, "切枪偷速松开停止");
        B(o, "切枪偷速触发开枪", false, "切枪偷速触发开枪");
        I(o, "切枪偷速间隔", 120, "切枪偷速循环间隔(毫秒)", null, 10, 5000);
        B(o, "高精度延时", false, "启用高精度延时",
            "这是一个精度更高的延时，所设置的【最低】延时数值必须根据游戏当前最低帧数赋予\n赋予公式：1000 ÷ 游戏当前最低帧数 = 延时数值");
        B(o, "自适应延时", false, "启用自适应延时",
            "实时根据游戏当前帧数计算出最低可用延时，并动态用于延时以达到类似防卡宏（需要管理员权限）");
        I(o, "自适应延时容错", 0, "自适应延时容错(毫秒)", "在自适应延时中加上该容错数值，可防止延时过低的问题", 0, 500);
        B(o, "保存日志", false, "保存调试日志");

        // ---------------- 连续左键 ----------------
        I(Sec.AutoClick, "间隔时间", 10, "连续左键间隔(毫秒)", "若为负数，则作为开关（再次点击鼠标左键或特殊触发键则停止）", -100000, 100000);

        // ---------------- 一键停止 ----------------
        B(Sec.StopMacro, "改为蜂鸣提示", false, "停止/锁定改为蜂鸣提示");
        T(Sec.StopMacro, "宏锁定白名单", string.Empty, "宏锁定白名单(功能名, 逗号分隔)", "锁定按键宏后仍然可用的功能");

        // ---------------- 小工具 ----------------
        var t = Sec.Tools;
        B(t, "热键仅在窗口存在时可用", false, "热键仅在小工具窗口存在时可用");
        C(t, "游戏断网方法", "防火墙规则断网", "游戏断网方法", new[] { "防火墙规则断网", "WFP规则断网", "禁用网络适配器" },
            "防火墙规则断网：通过系统防火墙规则断网，需要启用【使用中】的防火墙网络\nWFP规则断网：使用系统防火墙底层（WFP）直接断网，无视系统防火墙开关\n禁用网络适配器：直接禁用当前网络适配器");
        C(t, "断网规则", "全部断网", "断网规则", new[] { "全部断网", "仅断开存档(80,443)", "仅断开云存档", "仅断开交易" },
            "仅断开存档：无法上传数据但仍可以留在当前战局\n仅断开云存档：无法上传云存档但仍可以交易\n仅断开交易：无法进行交易但仍可以上传数据（借鉴 QuellGTA）");
        T(t, "网络适配器名称", string.Empty, "要禁用的网络适配器名称(空=自动)");
        B(t, "启用自动重连", true, "启用自动重连");
        I(t, "自动重连时间", 5, "自动重连时间(秒)", null, 1, 3600);
        C(t, "游戏卡单方法", "进程卡单", "游戏卡单方法", new[] { "进程卡单", "窗口菜单", "模拟窗口菜单" },
            "进程卡单：暂停游戏进程，期间无法操作游戏，适用于所有版本\n窗口菜单：发送按键消息呼出窗口菜单，适用于增强版（必须为游戏窗口模式）\n模拟窗口菜单：自动移动鼠标右击窗口边框呼出窗口菜单（必须为游戏窗口模式）");
        I(t, "游戏卡单时间", 8, "游戏卡单时间(秒)", "0 = 再次触发以结束卡单", 0, 600);
        I(t, "游戏限速速率", 1000, "游戏限速速率(KB/s)", null, 1, 10000000);
        C(t, "游戏限速协议类型", "全部", "游戏限速协议类型", new[] { "全部", "TCP", "UDP" });
        C(t, "游戏限速作用范围", "进程模式", "游戏限速作用范围", new[] { "进程模式", "路由模式" }, "进程模式：仅限速游戏进程（与检测到的加速器）\n路由模式：限速本机全部流量");
        T(t, "加速器进程", "UU,UUBooster,leigod,xunyou,qiyou", "加速器进程关键字(逗号分隔)");
        I(t, "随机密码长度", 10, "战局锁随机密码长度", "默认10，最大100", 1, 100);
        T(t, "密码存储", string.Empty, "已存储的战局锁密码(|分隔)", hidden: true);
        I(t, "开启提示音音频值", 470, "开启提示音频率(Hz)", null, 37, 32767);
        I(t, "开启提示音持续时间", 500, "开启提示音持续时间(毫秒)", null, 1, 5000);
        I(t, "关闭提示音音频值", 1000, "关闭提示音频率(Hz)", null, 37, 32767);
        I(t, "关闭提示音持续时间", 500, "关闭提示音持续时间(毫秒)", null, 1, 5000);
        B(t, "隐藏调试操作消息框", false, "隐藏调试操作消息框");
        B(t, "默认启动", false, "启动时打开小工具窗口");
        T(t, "联动昵称", "玩家", "联动昵称", "联动房间中显示给其他成员的名称，请勿填写真实信息");
        I(t, "房间端口", 19730, "房间端口", null, 1, 65535);
        T(t, "联动IP", "127.0.0.1:19730", "联动IP(地址:端口)");
        T(t, "黑名单列表", "{}", "联动黑名单", hidden: true);
        foreach (var tool in new[] { "结束进程", "游戏断网", "游戏卡单", "战局锁单", "游戏限速" })
        {
            B(t, "启用" + tool + "热键", false, "启用" + tool + "热键");
        }

        B(t, "结束进程同时结束登录器", true, "结束进程同时结束R星登录器");

        // ---------------- HUD ----------------
        var h = Sec.Hud;
        B(h, "启用小工具提示", true, "启用小工具提示");
        C(h, "小工具提示位置", "左上角", "小工具提示位置", new[] { "左上角", "右上角", "左下角", "右下角", "顶部居中" });
        C(h, "提示显示方式", "渐隐渐现", "提示显示方式", new[] { "渐隐渐现", "瞬隐瞬现", "常亮" });
        I(h, "提示停留时间", 2000, "提示停留(毫秒)", null, 200, 60000);
        I(h, "提示字号", 20, "提示字号", null, 8, 72);
        T(h, "计时器时间格式", "hh:mm:ss|:iii", "计时器时间格式", "hh 时, mm 分, ss 秒, iii 毫秒；| 后为小字号部分");
        C(h, "计时器位置", "顶部居中", "计时器位置", new[] { "左上角", "右上角", "左下角", "右下角", "顶部居中" });
        I(h, "计时器超时时间", 0, "计时器超时(秒, 0=不超时)", null, 0, 86400);

        // ---------------- 记忆开关 ----------------
        var sw = Sec.Switches;
        B(sw, "启用同步退出", false, "启用同步退出", "当游戏被关闭时，本软件也会同步关闭");
        B(sw, "释放WIN键", false, "释放WIN键", "在游戏前台时屏蔽 Win 键，防止误触切出游戏");
        B(sw, "输入法检测防误触", false, "输入法检测防误触", "游戏内非【美式键盘】(如聊天打字)时不会触发按键宏");
        B(sw, "优先读取游戏辅助键位", false, "优先读取游戏辅助键位", "读取 GTA 键位时优先使用第二组(辅助)键位");
        B(sw, "启用开机启动", false, "启用开机启动");
        B(sw, "启动时不弹出更新窗口", true, "启动时不弹出更新窗口", hidden: true);

        // ---------------- 设备 ----------------
        var dv = Sec.Device;
        T(dv, "游戏文件夹路径", string.Empty, "游戏文件夹路径");
        I(dv, "蜂鸣提示音量", -1, "蜂鸣提示音量(-1=系统音量)", null, -1, 100);
        I(dv, "界面自适应缩放", 100, "界面缩放(%)", "请重启软件来完全生效设置", 50, 300);
        I(dv, "已使用", 0, "启动次数", hidden: true);

        return l;
    }
}
