using System.Diagnostics;
using System.Net.NetworkInformation;
using NovaMacro.Core.Game;
using NovaMacro.Core.Network;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;
using NovaMacro.Platform.Native;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.SystemTools;

/// <summary>小工具 — kill game, network cut, solo session, session lock, speed limit.</summary>
public sealed class GameToolsService : IGameTools, IDisposable
{
    private readonly IGameWindow _game;
    private readonly SettingsStore _settings;
    private readonly INotifier _notifier;
    private readonly WfpBlocker _wfp = new();
    private readonly object _gate = new();
    private string? _disabledAdapter;
    private string? _cutMethod;
    private CancellationTokenSource? _reconnect;
    private CancellationTokenSource? _solo;
    private Action? _soloFinish;
    private bool _speedLimited;
    private bool _sessionLocked;

    public GameToolsService(IGameWindow game, SettingsStore settings, INotifier notifier)
    {
        _game = game;
        _settings = settings;
        _notifier = notifier;
    }

    public event EventHandler? StateChanged;

    public bool IsNetworkCut => _cutMethod is not null;

    public bool IsSolo => _solo is not null;

    public bool IsSessionLocked => _sessionLocked;

    public bool IsSpeedLimited => _speedLimited;

    private string? GamePath => _game.ExecutablePath ?? GtaInstall.FindExecutable(_settings.GetString(Sec.Device, "游戏文件夹路径"));

    // ---------------- 结束进程 ----------------
    public ToolResult KillGame()
    {
        var killed = 0;
        foreach (var name in GtaInstall.ProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        p.Kill(true);
                        killed++;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
        }

        if (killed == 0)
        {
            return Done(ToolResult.Fail("[结束进程] 游戏进程未运行！"));
        }

        if (_settings.GetBool(Sec.Tools, "结束进程同时结束登录器"))
        {
            foreach (var name in GtaInstall.LauncherProcessNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try
                        {
                            // Names like "Launcher" are generic: only kill Rockstar's own executables.
                            if (!IsRockstarProcess(p))
                            {
                                continue;
                            }

                            p.Kill(true);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                        }
                    }
                }
            }
        }

        _notifier.Beep(false);
        return Done(ToolResult.Ok("[结束进程] 已结束进程！"));
    }

    private static bool IsRockstarProcess(Process p)
    {
        try
        {
            var path = p.MainModule?.FileName;
            return path is not null && path.Contains("Rockstar", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Firewall rules have no effect on a profile whose firewall is turned off.</summary>
    private static string FirewallWarning()
    {
        var off = FirewallService.ProfileStates().Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        return off.Count == 0 ? string.Empty : $"（注意：{string.Join("、", off)}的防火墙未启用，规则在这些网络上无效；可在小工具中启用防火墙或改用 WFP规则断网）";
    }

    // ---------------- 游戏断网 ----------------
    public ToolResult ToggleNetworkCut() => SetNetworkCut(!IsNetworkCut);

    public ToolResult SetNetworkCut(bool cut)
    {
        lock (_gate)
        {
            _reconnect?.Cancel();
            _reconnect = null;
            if (!cut)
            {
                return Done(Restore());
            }

            if (IsNetworkCut)
            {
                return ToolResult.Ok("[游戏断网] 已处于断网状态");
            }

            var method = _settings.GetString(Sec.Tools, "游戏断网方法");
            ToolResult result;
            if (method == "禁用网络适配器")
            {
                var adapter = _settings.GetString(Sec.Tools, "网络适配器名称");
                if (string.IsNullOrWhiteSpace(adapter))
                {
                    adapter = ActiveAdapterName();
                }

                if (adapter is null)
                {
                    return Done(ToolResult.Fail("[游戏断网] 操作失败：该网络适配器异常或不可用！"));
                }

                var r = CommandRunner.Run("netsh", $"interface set interface name={CommandRunner.Quote(adapter)} admin=disabled");
                if (!r.Success)
                {
                    return Done(ToolResult.Fail("[游戏断网] 操作失败：该网络适配器异常或不可用！" + r.Output));
                }

                _disabledAdapter = adapter;
                result = ToolResult.Ok($"[游戏断网] 操作成功：已禁用网络适配器【{adapter}】！");
            }
            else
            {
                var path = GamePath;
                if (!_game.IsRunning || path is null)
                {
                    return Done(ToolResult.Fail("[游戏断网] 操作失败：游戏未运行！"));
                }

                var rule = _settings.GetString(Sec.Tools, "断网规则");
                var spec = NetworkRules.ForCut(rule);
                if (method == "WFP规则断网")
                {
                    var error = _wfp.Block(NetworkRules.CutRuleName, path, spec);
                    if (error is not null)
                    {
                        return Done(ToolResult.Fail("[游戏断网] 操作失败：" + error));
                    }
                }
                else
                {
                    var r = FirewallService.AddBlock(NetworkRules.CutRuleName, path, spec);
                    if (!r.Success)
                    {
                        return Done(ToolResult.Fail("[游戏断网] 操作失败：" + r.Output));
                    }
                }

                result = ToolResult.Ok($"[游戏断网] 操作成功：已创建【{rule}】规则！{NetworkRules.Describe(rule)}" + (method == "WFP规则断网" ? string.Empty : FirewallWarning()));
            }

            _cutMethod = method;
            _notifier.Beep(true);
            if (_settings.GetBool(Sec.Tools, "启用自动重连"))
            {
                ScheduleReconnect(_settings.GetInt(Sec.Tools, "自动重连时间"));
            }

            return Done(result);
        }
    }

    private void ScheduleReconnect(int seconds)
    {
        var cts = new CancellationTokenSource();
        _reconnect = cts;
        _ = Task.Run(async () =>
        {
            for (var left = seconds; left > 0; left--)
            {
                if (left <= 3)
                {
                    _notifier.Tip($"[游戏断网] 自动重连中：{left} 秒后...");
                }

                await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            }

            if (!cts.IsCancellationRequested)
            {
                var r = SetNetworkCut(false);
                _notifier.Tip(r.Message);
            }
        }, cts.Token);
    }

    private ToolResult Restore()
    {
        if (!IsNetworkCut)
        {
            FirewallService.Delete(NetworkRules.CutRuleName);
            _wfp.Unblock(NetworkRules.CutRuleName);
            return ToolResult.Ok("[游戏断网] 当前未断网");
        }

        if (_cutMethod == "禁用网络适配器" && _disabledAdapter is not null)
        {
            CommandRunner.Run("netsh", $"interface set interface name={CommandRunner.Quote(_disabledAdapter)} admin=enabled");
            _disabledAdapter = null;
        }
        else if (_cutMethod == "WFP规则断网")
        {
            _wfp.Unblock(NetworkRules.CutRuleName);
        }
        else
        {
            FirewallService.Delete(NetworkRules.CutRuleName);
        }

        _cutMethod = null;
        _notifier.Beep(false);
        return ToolResult.Ok("[游戏断网] 操作成功：已删除断网规则！");
    }

    public ToolResult SetTemporaryBlock(string ruleName, bool block)
    {
        var path = GamePath;
        if (path is null)
        {
            return ToolResult.Fail("游戏未运行");
        }

        var name = NetworkRules.HoverRuleName;
        if (!block)
        {
            _wfp.Unblock(name);
            return ToolResult.Ok($"[{ruleName}] 已恢复网络");
        }

        var error = _wfp.Block(name, path, NetworkRules.ForCut("全部断网"));
        return error is null ? ToolResult.Ok($"[{ruleName}] 已断网") : ToolResult.Fail(error);
    }

    private static string? ActiveAdapterName() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Count > 0)?.Name;

    // ---------------- 游戏卡单 ----------------
    public ToolResult ToggleSoloSession()
    {
        lock (_gate)
        {
            if (_solo is not null)
            {
                _solo.Cancel();
                return Done(ToolResult.Ok("[游戏卡单] 卡单完成！"));
            }

            if (!_game.IsRunning)
            {
                return Done(ToolResult.Fail("[游戏卡单] 操作失败：游戏未运行！"));
            }

            var method = _settings.GetString(Sec.Tools, "游戏卡单方法");
            var seconds = _settings.GetInt(Sec.Tools, "游戏卡单时间");
            if (method != "进程卡单" && !_game.IsWindowedMode)
            {
                return Done(ToolResult.Fail("[游戏卡单] 窗口菜单卡单必须为游戏窗口模式！"));
            }

            var cts = new CancellationTokenSource();
            _solo = cts;
            var pid = _game.ProcessId;
            var hwnd = _game.Handle;
            Action begin;
            Action end;
            string text;
            if (method == "进程卡单")
            {
                var handle = Kernel32.OpenProcess(Kernel32.PROCESS_SUSPEND_RESUME, false, (uint)pid);
                if (handle == 0)
                {
                    _solo = null;
                    return Done(ToolResult.Fail("[游戏卡单] 操作失败：无法打开游戏进程（需要管理员权限）"));
                }

                begin = () => Ntdll.NtSuspendProcess(handle);
                end = () =>
                {
                    Ntdll.NtResumeProcess(handle);
                    Kernel32.CloseHandle(handle);
                };
                text = "[游戏卡单] 正在进行进程卡单";
            }
            else if (method == "窗口菜单")
            {
                begin = () => PostMessageW(hwnd, WM_SYSCOMMAND, SC_KEYMENU, ' ');
                end = () => CloseSystemMenu(hwnd);
                text = "[游戏卡单] 正在进行窗口菜单卡单";
            }
            else
            {
                begin = () => RightClickTitleBar(hwnd);
                end = () => CloseSystemMenu(hwnd);
                text = "[游戏卡单] 正在进行模拟窗口菜单卡单";
            }

            var ended = 0;
            var finish = () =>
            {
                if (Interlocked.Exchange(ref ended, 1) == 0)
                {
                    end();
                }
            };
            _soloFinish = finish;
            begin();
            _notifier.Beep(true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (seconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    finish();
                    lock (_gate)
                    {
                        _solo = null;
                        _soloFinish = null;
                    }

                    _notifier.Beep(false);
                    _notifier.Tip("[游戏卡单] 卡单完成！");
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            });

            return ToolResult.Ok(seconds > 0 ? $"{text}（{seconds}秒）..." : $"{text}（再次触发以结束卡单）...");
        }
    }

    private static void CloseSystemMenu(nint hwnd)
    {
        var menu = FindWindowW("#32768", null);
        if (menu != 0)
        {
            PostMessageW(menu, 0x0100 /* WM_KEYDOWN */, 0x1B, 0);
            PostMessageW(menu, 0x0101 /* WM_KEYUP */, 0x1B, 0);
        }

        PostMessageW(hwnd, WM_CANCELMODE, 0, 0);
    }

    private static void RightClickTitleBar(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var r))
        {
            return;
        }

        var x = (r.Left + r.Right) / 2;
        var y = r.Top + 10;
        SetCursorPos(x, y);
        var output = new Input.WindowsInputOutput();
        output.Down(Core.Input.InputCodes.RightButton);
        output.Up(Core.Input.InputCodes.RightButton);
    }

    // ---------------- 战局锁单 ----------------
    public ToolResult ToggleSessionLock()
    {
        lock (_gate)
        {
            if (_sessionLocked)
            {
                FirewallService.Delete(NetworkRules.LockRuleName);
                _wfp.Unblock(NetworkRules.LockRuleName);
                _sessionLocked = false;
                _notifier.Beep(false);
                return Done(ToolResult.Ok("[战局锁单] 已解除战局锁单！"));
            }

            var path = GamePath;
            if (!_game.IsRunning || path is null)
            {
                return Done(ToolResult.Fail("[战局锁单] 操作失败：游戏未运行！"));
            }

            var useWfp = _settings.GetString(Sec.Tools, "游戏断网方法") == "WFP规则断网";
            if (useWfp)
            {
                var error = _wfp.Block(NetworkRules.LockRuleName, path, NetworkRules.SessionLock);
                if (error is not null)
                {
                    return Done(ToolResult.Fail("[战局锁单] 操作失败：" + error));
                }
            }
            else
            {
                var r = FirewallService.AddBlock(NetworkRules.LockRuleName, path, NetworkRules.SessionLock);
                if (!r.Success)
                {
                    return Done(ToolResult.Fail("[战局锁单] 操作失败：" + r.Output));
                }
            }

            _sessionLocked = true;
            _notifier.Beep(true);
            return Done(ToolResult.Ok("[战局锁单] 已创建 GTA5战局锁单 规则！规则仅阻止玩家链接！注意：锁单期间你无法Home差传。" + (useWfp ? string.Empty : FirewallWarning())));
        }
    }

    // ---------------- 游戏限速 ----------------
    public ToolResult ToggleSpeedLimit()
    {
        lock (_gate)
        {
            if (_speedLimited)
            {
                RemoveQos();
                _speedLimited = false;
                _notifier.Beep(false);
                return Done(ToolResult.Ok("[游戏限速] 操作成功：已停止游戏限速！"));
            }

            if (!_game.IsRunning)
            {
                return Done(ToolResult.Fail("[游戏限速] 操作失败：游戏未运行！"));
            }

            var rate = NetworkRules.ThrottleBitsPerSecond(_settings.GetInt(Sec.Tools, "游戏限速速率"));
            var protocol = _settings.GetString(Sec.Tools, "游戏限速协议类型") switch
            {
                "TCP" => "TCP",
                "UDP" => "UDP",
                _ => "Both",
            };
            var routeMode = _settings.GetString(Sec.Tools, "游戏限速作用范围") == "路由模式";
            RemoveQos();
            var exe = Path.GetFileName(GamePath ?? GtaInstall.EnhancedExe);
            var script = routeMode
                ? $"New-NetQosPolicy -Name {CommandRunner.PsQuote(NetworkRules.QosGameName)} -Default -IPProtocolMatchCondition {protocol} -ThrottleRateActionBitsPerSecond {rate} -PolicyStore ActiveStore | Out-Null"
                : $"New-NetQosPolicy -Name {CommandRunner.PsQuote(NetworkRules.QosGameName)} -AppPathNameMatchCondition {CommandRunner.PsQuote(exe)} -IPProtocolMatchCondition {protocol} -ThrottleRateActionBitsPerSecond {rate} -PolicyStore ActiveStore | Out-Null";
            var r = CommandRunner.PowerShell(script);
            if (!r.Success)
            {
                return Done(ToolResult.Fail("[游戏限速] 操作失败：" + r.Output));
            }

            var accelerator = routeMode ? null : FindAccelerator();
            var extra = string.Empty;
            if (accelerator is not null)
            {
                CommandRunner.PowerShell($"New-NetQosPolicy -Name {CommandRunner.PsQuote(NetworkRules.QosAcceleratorName)} -AppPathNameMatchCondition {CommandRunner.PsQuote(accelerator)} -IPProtocolMatchCondition {protocol} -ThrottleRateActionBitsPerSecond {rate} -PolicyStore ActiveStore | Out-Null");
                extra = $" 检测到游戏加速器，已同时限速！（{accelerator}）";
            }

            _speedLimited = true;
            _notifier.Beep(true);
            return Done(ToolResult.Ok("[游戏限速] 操作成功：已开始游戏限速！" + extra));
        }
    }

    private static void RemoveQos()
    {
        CommandRunner.PowerShell(
            $"Remove-NetQosPolicy -Name {CommandRunner.PsQuote(NetworkRules.QosGameName)} -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue;" +
            $"Remove-NetQosPolicy -Name {CommandRunner.PsQuote(NetworkRules.QosAcceleratorName)} -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue");
    }

    private string? FindAccelerator()
    {
        var keywords = _settings.GetString(Sec.Tools, "加速器进程").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (keywords.Any(k => p.ProcessName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return p.ProcessName + ".exe";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Restores everything NovaMacro changed (exit). Resuming a suspended game is done synchronously:
    /// the app may terminate right after this call.
    /// </summary>
    public void CleanupAll()
    {
        _reconnect?.Cancel();
        _soloFinish?.Invoke();
        _solo?.Cancel();
        if (_disabledAdapter is not null)
        {
            CommandRunner.Run("netsh", $"interface set interface name={CommandRunner.Quote(_disabledAdapter)} admin=enabled");
            _disabledAdapter = null;
        }

        if (_cutMethod is not null && _cutMethod != "WFP规则断网")
        {
            FirewallService.Delete(NetworkRules.CutRuleName);
        }

        if (_sessionLocked)
        {
            FirewallService.Delete(NetworkRules.LockRuleName);
        }

        if (_speedLimited)
        {
            RemoveQos();
        }

        _wfp.Dispose();
        _cutMethod = null;
        _sessionLocked = false;
        _speedLimited = false;
    }

    /// <summary>
    /// Removes rules/policies a previous run may have left behind after a crash (startup, background).
    /// </summary>
    public void CleanupStale()
    {
        FirewallService.Delete(NetworkRules.CutRuleName);
        FirewallService.Delete(NetworkRules.LockRuleName);
        RemoveQos();
    }

    public void Dispose() => CleanupAll();

    private ToolResult Done(ToolResult result)
    {
        if (!_settings.GetBool(Sec.Tools, "隐藏调试操作消息框"))
        {
            _notifier.Log(result.Message);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
