# 功能对照：GTAOL 按键宏 7.6.5 → NovaMacro 2.0

“状态”含义：

- **一致**：行为与原软件相同或等价（设置键名相同）。
- **等价实现**：目标与交互相同，但原软件依赖的屏幕识别细节无法从二进制中完整还原，改为可配置的实现。
- **需实测校准**：逻辑已实现，但坐标/菜单位置/阈值需要在游戏内验证后微调（都暴露在设置中）。
- **未实现**：说明原因。

| 原功能 | NovaMacro 实现 | 状态 | 说明 |
| --- | --- | --- | --- |
| 武器卡弹（默认/自动速射/单次切枪/开火激活） | `WeaponJamFeature` | 一致 | `[武器卡弹设置]` 全部键名沿用；支持宏模式独立设置、各武器速射间隔、开火顺序、宏结束操作、武器键位/轮盘式切换、功能键轮换欲切枪武器、辅助键轮换宏模式、锁定宏模式 |
| 买满子弹（全部 / 当前武器） | `BuyAmmoFeature` + `买满子弹路径`/`买当前子弹路径` | 需实测校准 | 原软件的“自适应”通过识别菜单像素循环左右选择；此处使用可编辑路径（默认 = 你旧版已验证的序列） |
| 菜单零食 / 菜单护甲 | `MenuSnackFeature` | 需实测校准 | 个数为 0 时按住持续吃；辅助键 + 热键穿戴所选护甲 |
| 载具启动（叫出/收回/引擎/车门/全部） | `VehicleControlFeature` | 需实测校准 | 与原版一致：单击/双击执行设定操作（默认控制引擎），辅助键 + 单击收回、辅助键 + 双击叫出；可停留在引擎界面 |
| 呼叫载具（个人载具、机动作战中心、复仇者、恐霸、虎鲸、致幻剂实验室） | `ServiceVehicleFeature` | 需实测校准 | 单击呼叫、双击送回、长按内部载具、辅助键轮换；序号可配置 |
| 快速人称 | `QuickPerspectiveFeature` | 一致 | 记录当前人称；辅助键重置到第一人称 |
| 电话呼叫 | `PhoneCallFeature` | 需实测校准 | 按住热键 + 数字选择联系人，负数位置向上滚动 |
| 连续左键 | `AutoClickFeature` | 一致 | 大写键 + 双击左键；负数间隔为开关模式 |
| 轮盘零食 / 快速零食穿护甲 | `WheelSnackFeature` | 一致 | 支持“按住键位触发 / 侧键上 / 侧键下” |
| 切枪偷速 | `WeaponSwapSpeedFeature` | 等价实现 | 键/功能键/副键、瞄准续偷速、松开停止、触发开枪 |
| AD 偷速 | `AdSpeedFeature` | 一致 | Shift + W + E |
| 瞬间悬停（断网悬停） | `InstantHoverFeature` | 需实测校准 | 辅助键 + 角色选择键；断网悬停使用 WFP 临时规则 |
| 切换战局（公开/邀请/好友） | `SwitchSessionFeature` | 需实测校准 | 原软件用鼠标点击 + 识别制作器/线上/故事模式界面；此处为可编辑键盘路径 |
| 奔跑切换 | `SprintToggleFeature` | 一致 | 长按超过 1 秒视为不切换 |
| 延迟攀爬（自动切空手、自动相机、二次预选相机） | `DelayClimbFeature` | 等价实现 | “相机状态检测”需要识别相机界面，未实现（选项已隐藏） |
| 替代回车（Ctrl+空格 / 大写+空格 / 鼠标中键） | `EnterAlternativeFeature` | 一致 | |
| 挂机 / 空闲挂机 | `AfkFeature` | 一致 | 点击/按住、间隔；空闲时间可配置（默认 10 分钟） |
| 赌场指纹 | `CasinoFingerprintSolver` | 需实测校准 | 使用原软件内置的 16 个 64 位指纹哈希与 2560x1440 布局（643,366 起，150px 单元，42px 间隔）；哈希算法采用均值哈希，阈值可调 |
| 赌场门禁显示 | — | 未实现 | 原功能捕获门禁“蓝点”密码组并弹窗显示，像素规则无法从二进制可靠还原 |
| 佩岛指纹 | `CayoFingerprintSolver` | 需实测校准 | 8 条切片逐一旋转比对目标指纹；目标/拼图区域可在设置中调整 |
| 自动梅杜莎 | `MedusaAutopilot` | 需实测校准 | 紫色标点颜色 15879332、水平线颜色 789516 沿用；偏航纠正 + 高度区间控制 |
| 地图差传（BOT 法 / 听声法 / 读秒法） | `MapTeleportFeature` | 等价实现 | 发送 Steam 战局链接（与原版相同以 %3D 编码）或好友列表找图点击加入，之后确认阶段；读取 `Steam\logs\console_log.txt` 中最近的 `ExecuteSteamURL` 链接；听声法使用系统输出音量，可静音其他进程。原软件的 QuellGTA-SteamJVP 在线更新服务器未接入 |
| 任务卡传 | `JobTeleportFeature` | 需实测校准 | 快速加入差事路径 → 等待（M 键跳过）→ 注册 CEO/摩托帮；“未购买服装厂/自动检测注册状态”需要屏幕识别，未实现（选项已隐藏） |
| 内置截图 | `ScreenshotFeature` | 一致 | 复制游戏客户区到剪贴板 |
| 外置准星 | `CrosshairWindow` | 一致 | Ctrl+Z 显示/隐藏；样式、偏移、像素对齐；“嵌入窗口”未实现（以置顶穿透窗口显示，选项已隐藏） |
| 自定义宏 | `CustomMacroFeature` + `MacroScript` | 等价实现 | 分类目录、XML 存储、热键、循环、锁定时可用、点击/按住/开关；脚本语法见编辑器 |
| 一键停止 / 锁定 / 白名单 | `MacroRuntime` | 一致 | 单击停止，长按锁定；可改为蜂鸣提示 |
| 结束进程（同时结束 R 星登录器） | `GameToolsService.KillGame` | 一致 | 只结束路径属于 Rockstar 的登录器进程 |
| 游戏断网（防火墙 / WFP / 网络适配器；QuellGTA 规则；自动重连） | `GameToolsService` + `WfpBlocker` | 一致 | WFP 使用动态会话，程序退出时规则自动消失 |
| 游戏卡单（进程 / 窗口菜单 / 模拟窗口菜单） | `GameToolsService.ToggleSoloSession` | 一致 | 时间为 0 时再次触发结束；程序退出时一定恢复游戏进程 |
| 战局锁单 | `GameToolsService.ToggleSessionLock` | 一致 | 仅阻止 UDP 6672 |
| 游戏限速（进程/路由模式、协议、加速器检测） | `GameToolsService.ToggleSpeedLimit` | 一致 | `New-NetQosPolicy`，速率单位 KB/s |
| 战局锁（startup.meta 密码、随机密码、密码存储） | `SessionLockFile` | 一致 | 使用原软件的增强版/传承版模板 |
| 防火墙状态 | `FirewallService.ProfileStates` | 一致 | |
| 小工具热键（结束进程/断网/卡单/锁单/限速） | `ToolHotkeyFeature` | 一致 | 与原版相同存放在 `[小工具配置] 结束进程热键/…功能键/…副键`；支持“热键仅在窗口存在时可用” |
| 联动（创建/加入房间、聊天、断网/卡单/断进程联动、踢出/拉黑） | `LinkRoom` | 等价实现 | 自有 JSON 行协议，与原软件不互通 |
| HUD 提示与计时器 | `HudWindow` | 一致 | 位置、渐隐渐现/瞬隐瞬现/常亮、`hh:mm:ss\|:iii` 格式、超时变色 |
| 高精度延时 / 自适应延时 | `DelayProvider` + `FpsMonitor` | 一致 | ETW DXGI Present 事件统计帧率 |
| 输入法检测防误触 / 释放 WIN 键 / 同步退出 / 开机启动 | `MacroRuntime` / `GlobalInputHook` / `AppHost` | 一致 | 开机启动使用任务计划（最高权限），因为需要管理员权限的程序不能通过 Run 注册表项自启动 |
| 游戏文件夹路径自动寻找 | `GtaLocator` | 一致 | 运行中的进程、注册表（Steam/Epic）、常见目录 |
| 读取游戏键位 / 优先读取辅助键位 | `GtaControlFile` | 一致 | 解析 `control\user.xml` |
| 键位修改器（外部程序） | — | 未实现 | 原软件调用独立的“GTAV 键位修改器.exe”；NovaMacro 改为在“游戏键位”页覆盖键位 |
| 检查更新 / 下载 / 修改 DNS / 卸载脚本 / 赞助 | — | 未实现 | 属于原发布渠道的功能，与本项目无关 |
| 界面自适应缩放 | WPF 原生 DPI | 等价实现 | Per-Monitor V2 |
