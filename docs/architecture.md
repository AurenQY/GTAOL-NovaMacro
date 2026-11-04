# 架构

```
NovaMacro.App (WPF)            界面、组合根 AppHost、覆盖层、托盘
   │
   ├── NovaMacro.Platform      Windows：SendInput、低级钩子、XInput、窗口跟踪、截图、
   │                           netsh/WFP/QoS、进程暂停、ETW 帧率、WASAPI
   │
   └── NovaMacro.Core          平台无关：设置、运行时、功能、求解器、协议
```

## 数据流

1. `GlobalInputHook` 在独立消息循环线程上接收物理（非注入）键鼠事件和 XInput 手柄按钮，
   维护按下状态，并同步调用 `MacroRuntime.HandleInput(code, down, repeat)`；返回 `true` 时吞掉事件。
2. `MacroRuntime`：
   - 读取每个功能的热键（`<功能>热键` 节的 键/功能键/副键）与开关（`[记忆开关]`），在设置变化时刷新；
   - 过滤：游戏前台、输入法检测、锁定/白名单；
   - 热键匹配：修饰键最“具体”的组合优先；带修饰键的热键会吞掉主键；
   - 需要单击/双击/长按区分的功能通过 `ClickClassifier` 得到手势（每 15ms `Tick` 轮询）；
   - 原始输入也会交给每个功能的 `OnInput`（AD 偷速、切换战局、替代回车等组合触发）。
3. 功能在自己的工作线程中运行（`MacroFeature.Start`），同一功能不会重入；一键停止取消所有令牌并释放所有按键。
4. 功能通过 `GameInput` 以“游戏动作”发送按键（`GameAction` → 用户 GTA 键位），
   延时经过 `DelayProvider`（>0 受自适应延时约束，0 不延时，<0 绝对延时）。
5. 菜单类宏全部由 `[M菜单布局]` 中的路径驱动（`MenuPath` 解析，`MenuRunner` 执行）。

## 设置

- `SettingsSchema` 声明每个键的类型、默认值、范围、说明，设置界面由它自动生成。
- 取值顺序：`Settings.ini` → 内置默认（作者的 Settings.ini）→ 模式默认值。
- 布尔值沿用原软件的 `真/假`。

## 系统工具的安全性

- WFP 规则使用动态会话：NovaMacro 退出（包括崩溃）时过滤器自动删除。
- 防火墙规则、QoS 策略在启动和退出时都会清理（`GameToolsService.CleanupAll`）。
- 游戏卡单使用 `NtSuspendProcess`，到时或再次触发必定 `NtResumeProcess`。
