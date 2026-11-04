# 逆向分析记录（GTAOL 按键宏 7.6.5.261024）

用于确定 NovaMacro 2.0 功能范围与默认参数。只提取功能规格、常量和数据，没有复用原程序代码。

## 样本

- 文件：`GTAOL____.exe`，751,104 字节，SHA-256 `2d2311877e90e06313333359daeba6b643739eb551d8e452cf4615d21899b33d`
- PE32 GUI，UPX 4.21 加壳（方法 `0x0e` = LZMA，过滤器 `0x24`/cto `0x15`），版本信息 `1.0.24.217`，公司 `OnelyMaker`。
- 易语言（静态编译，支持库：系统核心、特殊功能、应用接口、扩展界面三、扩展功能一、操作系统界面、OpenGL）。

## 解包方法

1. 读取 `UPX!` 头：`u_len=0x317021`、`c_len=0xa1341`、LZMA 参数 `lc=3 lp=0 pb=2`。
2. 用 Python `lzma`（RAW/LZMA1）解压 `UPX1` 段起始的数据，得到从 RVA `0x1000` 开始的镜像。
3. 还原 call/jmp 过滤：`E8/E9` 后首字节等于 cto 时，后三字节为大端 24 位绝对值，`rel = abs - (offset + 1)`。
4. 用 capstone 反汇编，按 `push/mov imm32` 交叉引用字符串（GBK），按函数分组。

（参考了 morluto/rea 的分析流程思路；本次分析未运行原程序。）

## 主要发现

- 配置：`Settings.ini`（GBK），节/键名称即 NovaMacro 采用的名称；热键节 `<功能>热键` 含 `键/功能键/副键`。
- 游戏键位表：结构体偏移 → 动作（暂停菜单、HOME、M菜单、上下左右/选择/返回、手机、移动、奔跑、掩护、射击、瞄准、
  武器轮盘与各武器栏位、轮盘零食/护甲、切换人称、角色选择、飞机控制），读取自
  `control\user.xml`（`rage__ControlInput__ControlSettings/Mappings/Item`），KEY_* 虚拟码对照表与 NovaMacro 相同。
- 断网规则（借鉴 QuellGTA）：`80,443`；云存档 `cs-gta5-prod.ros.rockstargames.com`、`ps-gta5-prod.ros.rockstargames.com`；
  交易 `prod.p01sjc.pod.rockstargames.com`、`prod.p02sjc.pod.rockstargames.com`、`185.56.65.200`、`185.56.65.201`。
- 方法：防火墙规则（netsh/注册表）、WFP（`FwpmEngineOpen0`/`FwpmFilterAdd0`）、禁用网卡（`netsh interface set interface`）。
- 战局锁单：阻止 UDP `6672`。游戏限速：WMI `MSFT_NetQosPolicySettingData`，策略名 `…_QOS限速_游戏/加速器`。
- 游戏卡单：`ZwSuspendProcess/ZwResumeProcess`，或打开窗口系统菜单（`#32768`）。
- 战局锁：`x64\data\startup.meta`，增强版/传承版两套模板，密码写在 XML 注释中。
- 地图差传：`steam://rungame/3240220/<SteamID>/-steamjvp=…==`，从 `Steam\logs\console_log.txt` 的
  `ExecuteSteamURL` 读取；参考图片 `MapTeleportBot\BOT1.BMP/BOT2.BMP`。
- 赌场指纹：16 个 64 位二进制哈希；布局常量以 1440 高度为基准：`643,366,150,42`（以及 `674,393,837,711`）；
  操作键 `↓(40) →(39) 回车(13) Tab(9)`。
- 自动梅杜莎：偏航纠正（左右偏航短按 100ms）、高度控制（爬升/俯冲 200/500/700ms）。
- 自适应延时：ETW 会话 `_Frame_Trace_Sample_`（需要管理员）；高精度延时公式 `1000 ÷ 最低帧数`。
