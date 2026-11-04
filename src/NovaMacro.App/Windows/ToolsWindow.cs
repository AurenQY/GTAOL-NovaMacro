using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Link;
using NovaMacro.Core.Network;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Windows;

/// <summary>小工具 — system tools, session password lock, firewall state, link room and timer.</summary>
public sealed class ToolsWindow : ThemedWindow
{
    private readonly AppHost _host;
    private readonly Dictionary<FeatureId, Button> _toolButtons = new();
    private readonly TextBox _password = new() { Width = 150 };
    private readonly ComboBox _stored = new() { Width = 150 };
    private readonly TextBlock _lockInfo = Ui.Text(string.Empty, dim: true);
    private readonly StackPanel _firewall = new() { Orientation = Orientation.Horizontal };
    private readonly ListBox _members = new() { Height = 210, Margin = new Thickness(2) };
    private readonly TextBox _chatLog = new() { IsReadOnly = true, Height = 210, VerticalContentAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _chatInput = new() { Width = 180 };
    private readonly TextBlock _linkStatus = Ui.Text("连接状态：未连接", dim: true);

    public ToolsWindow(AppHost host)
    {
        _host = host;
        Title = "小工具";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowPositions.Restore(this, host.Settings, "小工具");
        Deactivated += (_, _) => WindowPositions.Save(this, host.Settings, "小工具");
        Closing += (_, _) => WindowPositions.Save(this, host.Settings, "小工具");

        var left = new StackPanel { Width = 430 };
        left.Children.Add(BuildTools());
        left.Children.Add(BuildSessionLock());
        left.Children.Add(BuildFirewall());
        left.Children.Add(Ui.Card(new WrapPanel
        {
            Children =
            {
                Ui.Btn("计时器", host.Overlays.ToggleTimer, tip: "开始/停止 HUD 计时器"),
                Ui.Btn("外置准星", host.Overlays.ToggleCrosshair, tip: "显示/隐藏外置准星"),
                Ui.Btn("小工具设置", () => new SettingsWindow(host, Sec.Tools) { Owner = this }.Show()),
                Ui.Btn("HUD设置", () => new SettingsWindow(host, Sec.Hud) { Owner = this }.Show()),
            },
        }, "HUD辅助"));
        var right = new StackPanel { Width = 380 };
        right.Children.Add(BuildLink());
        Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4), Children = { left, right } };

        host.Tools.StateChanged += (_, _) => Dispatcher.BeginInvoke(UpdateTools);
        host.Link.Status += OnLinkStatus;
        host.Link.Chat += OnChat;
        host.Link.MembersChanged += OnMembers;
        Closed += (_, _) =>
        {
            host.Link.Status -= OnLinkStatus;
            host.Link.Chat -= OnChat;
            host.Link.MembersChanged -= OnMembers;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => UpdateTools();
        timer.Start();
        UpdateTools();
        RefreshLock();
        RefreshFirewall();
    }

    private Border BuildTools()
    {
        var wrap = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var id in new[] { FeatureId.KillGame, FeatureId.NetworkCut, FeatureId.SoloSession, FeatureId.SessionLock, FeatureId.SpeedLimit })
        {
            var def = FeatureCatalog.Get(id);
            var button = new Button { ToolTip = def.Help };
            button.Click += (_, _) => RunTool(id);
            _toolButtons[id] = button;

            var enable = new CheckBox { Content = Ui.Text(_host.Settings.GetHotkey(def).ToString().Replace("键", string.Empty), dim: true, size: 11, wrap: false), ToolTip = "启用该工具的热键", IsChecked = _host.Settings.GetBool(Sec.Tools, "启用" + def.Name + "热键") };
            enable.Checked += (_, _) => _host.Settings.SetBool(Sec.Tools, "启用" + def.Name + "热键", true);
            enable.Unchecked += (_, _) => _host.Settings.SetBool(Sec.Tools, "启用" + def.Name + "热键", false);
            var cell = Ui.Col(button, enable);
            cell.Margin = new Thickness(2, 0, 2, 4);
            wrap.Children.Add(cell);
        }

        return Ui.Card(Ui.Col(
            wrap,
            Ui.Text($"断网：{_host.Settings.GetString(Sec.Tools, "游戏断网方法")} / {_host.Settings.GetString(Sec.Tools, "断网规则")} · 卡单：{_host.Settings.GetString(Sec.Tools, "游戏卡单方法")} {_host.Settings.GetInt(Sec.Tools, "游戏卡单时间")}秒", dim: true, size: 11)), "游戏工具");
    }

    private void RunTool(FeatureId id)
    {
        if (id == FeatureId.KillGame && !Dialog.Confirm(this, "确定结束进程？", "确定操作？"))
        {
            return;
        }

        var tools = _host.Tools;
        _ = Task.Run(() =>
        {
            var r = id switch
            {
                FeatureId.KillGame => tools.KillGame(),
                FeatureId.NetworkCut => tools.ToggleNetworkCut(),
                FeatureId.SoloSession => tools.ToggleSoloSession(),
                FeatureId.SessionLock => tools.ToggleSessionLock(),
                _ => tools.ToggleSpeedLimit(),
            };
            _host.Notifier.Tip(r.Message);
            Dispatcher.BeginInvoke(UpdateTools);
        });
    }

    private void UpdateTools()
    {
        var t = _host.Tools;
        Set(FeatureId.KillGame, "结束进程", false);
        Set(FeatureId.NetworkCut, t.IsNetworkCut ? "恢复网络" : "游戏断网", t.IsNetworkCut);
        Set(FeatureId.SoloSession, t.IsSolo ? "结束卡单" : "游戏卡单", t.IsSolo);
        Set(FeatureId.SessionLock, t.IsSessionLocked ? "解除锁单" : "战局锁单", t.IsSessionLocked);
        Set(FeatureId.SpeedLimit, t.IsSpeedLimited ? "停止限速" : "游戏限速", t.IsSpeedLimited);
        foreach (var b in _toolButtons.Values)
        {
            b.IsEnabled = _host.Game.IsRunning || b == _toolButtons[FeatureId.NetworkCut] || b == _toolButtons[FeatureId.SpeedLimit];
        }
    }

    private void Set(FeatureId id, string text, bool active)
    {
        var b = _toolButtons[id];
        b.Content = text;
        b.BorderBrush = Ui.Res(active ? "Warn" : "Border");
    }

    // ---------------- 战局锁 ----------------
    private Border BuildSessionLock()
    {
        _stored.SelectionChanged += (_, _) =>
        {
            if (_stored.SelectedItem is string s)
            {
                _password.Text = s;
            }
        };
        var row1 = Ui.Row(Ui.Text("密码"), _password,
            Ui.Btn("随机", () => _password.Text = SessionLockFile.RandomPassword(_host.Settings.GetInt(Sec.Tools, "随机密码长度")), tip: "随机密码长度可在小工具设置中修改"),
            Ui.Btn("添加", AddLock, accent: true, tip: "添加战局锁（需重新登录游戏）"),
            Ui.Btn("移除", RemoveLock, tip: "移除战局锁（需重新登录游戏）"));
        var row2 = Ui.Row(Ui.Text("存储"), _stored, Ui.Btn("存储当前", StorePassword), Ui.Btn("删除", DeleteStored));
        var card = Ui.Card(Ui.Col(row1, row2, _lockInfo), "战局锁");
        card.ToolTip = "战局锁会在游戏目录写入 x64\\data\\startup.meta，相同密码的玩家会被匹配进同一个公开战局。修改后需要重新登录游戏。";
        return card;
    }

    private string? GameFolder
    {
        get
        {
            var folder = _host.Settings.GetString(Sec.Device, "游戏文件夹路径");
            if (GtaInstall.FindExecutable(folder) is null)
            {
                folder = GtaLocator.Find(_host.Game) ?? string.Empty;
                if (folder.Length > 0)
                {
                    _host.Settings.Set(Sec.Device, "游戏文件夹路径", folder);
                }
            }

            return folder.Length > 0 ? folder : null;
        }
    }

    private GtaEdition FolderEdition(string folder) =>
        File.Exists(Path.Combine(folder, GtaInstall.EnhancedExe)) ? GtaEdition.Enhanced : GtaEdition.Legacy;

    private void RefreshLock()
    {
        var folder = GameFolder;
        if (folder is null)
        {
            _lockInfo.Text = "战局锁（无路径不可用）：未找到游戏目录路径";
            return;
        }

        var current = SessionLockFile.Read(folder);
        _lockInfo.Text = current is null ? $"当前没有战局锁（{folder}）" : $"[战局锁] 发现战局锁密码：{current}";
        _stored.Items.Clear();
        foreach (var p in StoredPasswords())
        {
            _stored.Items.Add(p);
        }
    }

    private List<string> StoredPasswords() =>
        _host.Settings.GetString(Sec.Tools, "密码存储").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    private void AddLock()
    {
        var folder = GameFolder;
        if (folder is null)
        {
            Dialog.Show(this, "未找到游戏目录路径");
            return;
        }

        try
        {
            SessionLockFile.Write(folder, _password.Text, FolderEdition(folder));
            _host.Notifier.Tip("[战局锁] 操作成功：战局锁已添加！请重新登录游戏！");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Dialog.Show(this, "[战局锁] 操作失败：" + ex.Message);
        }

        RefreshLock();
    }

    private void RemoveLock()
    {
        var folder = GameFolder;
        if (folder is null)
        {
            return;
        }

        _host.Notifier.Tip(SessionLockFile.Remove(folder) ? "[战局锁] 操作成功：战局锁已移除！请重新登录游戏！" : "[战局锁] 操作失败：战局锁不存在！");
        RefreshLock();
    }

    private void StorePassword()
    {
        var p = _password.Text.Trim();
        if (p.Length == 0)
        {
            Dialog.Show(this, "[战局锁] 操作失败：请输入密码！");
            return;
        }

        var list = StoredPasswords();
        if (list.Contains(p))
        {
            Dialog.Show(this, "[战局锁] 操作失败：密码已存在！");
            return;
        }

        list.Add(p);
        _host.Settings.Set(Sec.Tools, "密码存储", string.Join("|", list));
        RefreshLock();
    }

    private void DeleteStored()
    {
        if (_stored.SelectedItem is not string p)
        {
            return;
        }

        _host.Settings.Set(Sec.Tools, "密码存储", string.Join("|", StoredPasswords().Where(x => x != p)));
        _host.Log.Write("[战局锁] 已删除存储密码：" + p);
        RefreshLock();
    }

    // ---------------- 防火墙状态 ----------------
    private Border BuildFirewall()
    {
        var card = Ui.Card(_firewall, "防火墙状态");
        card.ToolTip = "【防火墙规则断网】需要启用当前使用中的防火墙网络；【WFP规则断网】不受影响。";
        return card;
    }

    private void RefreshFirewall()
    {
        _ = Task.Run(() =>
        {
            var states = FirewallService.ProfileStates();
            Dispatcher.BeginInvoke(() =>
            {
                _firewall.Children.Clear();
                foreach (var (name, on) in states)
                {
                    var profile = name;
                    var cb = new CheckBox { Content = Ui.Text("活动" + profile), IsChecked = on };
                    cb.Click += (_, _) =>
                    {
                        var r = FirewallService.SetProfileState(profile, cb.IsChecked == true);
                        _host.Notifier.Tip(r.Success ? $"[防火墙状态] 操作成功！已{(cb.IsChecked == true ? "启用" : "关闭")}{profile}" : "[防火墙状态] 操作失败！" + r.Output);
                        RefreshFirewall();
                    };
                    _firewall.Children.Add(cb);
                }
            });
        });
    }

    // ---------------- 联动 ----------------
    private Border BuildLink()
    {
        var s = _host.Settings;
        var nick = new TextBox { Text = s.GetString(Sec.Tools, "联动昵称"), Width = 110 };
        var ip = new TextBox { Text = s.GetString(Sec.Tools, "联动IP"), Width = 140 };
        var port = new TextBox { Text = s.GetInt(Sec.Tools, "房间端口").ToString(), Width = 56 };
        nick.LostFocus += (_, _) => s.Set(Sec.Tools, "联动昵称", nick.Text.Trim());
        ip.LostFocus += (_, _) => s.Set(Sec.Tools, "联动IP", ip.Text.Trim());
        port.LostFocus += (_, _) =>
        {
            if (int.TryParse(port.Text, out var p))
            {
                s.SetInt(Sec.Tools, "房间端口", p);
            }
        };
        _host.Link.SetBlacklist(s.GetString(Sec.Tools, "黑名单列表").Trim('{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var connectInfo = Ui.Row(Ui.Text("昵称"), nick, Ui.Text("IP"), ip, Ui.Text("端口"), port);
        var connect = Ui.Row(
            Ui.Btn("创建房间", () =>
            {
                try
                {
                    _host.Link.Host(int.TryParse(port.Text, out var p) ? p : 19730, nick.Text.Trim());
                }
                catch (Exception ex)
                {
                    Dialog.Show(this, ex.Message, "联动");
                }
            }),
            Ui.Btn("加入房间", async () =>
            {
                try
                {
                    await _host.Link.JoinAsync(ip.Text, nick.Text.Trim());
                }
                catch (Exception ex)
                {
                    _host.Log.Write("[联动功能] 连接失败！" + ex.Message);
                }
            }),
            Ui.Btn("断开", () => _host.Link.Leave()));

        var actions = new WrapPanel();
        actions.Children.Add(Ui.Text("联动："));
        foreach (var kind in LinkKinds.All)
        {
            var k = kind;
            actions.Children.Add(Ui.Btn(k, async () => await _host.Link.TriggerLinkAsync(k), tip: "房间内所有成员（包括自己）同时执行"));
        }

        actions.Children.Add(Ui.Btn("踢出", async () =>
        {
            if (_members.SelectedItem is string m)
            {
                await _host.Link.KickAsync(m, blacklist: false);
            }
        }));
        actions.Children.Add(Ui.Btn("拉黑", async () =>
        {
            if (_members.SelectedItem is string m)
            {
                await _host.Link.KickAsync(m, blacklist: true);
                s.Set(Sec.Tools, "黑名单列表", "{" + string.Join(",", _host.Link.Blacklist) + "}");
            }
        }));

        var chat = Ui.Row(_chatInput, Ui.Btn("发送", async () =>
        {
            if (_chatInput.Text.Length > 0)
            {
                await _host.Link.SendChatAsync(_chatInput.Text);
                _chatInput.Clear();
            }
        }));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var membersPanel = Ui.Col(Ui.Text("当前在线人数：", dim: true), _members);
        Grid.SetColumn(membersPanel, 0);
        var chatPanel = Ui.Col(_chatLog, chat);
        Grid.SetColumn(chatPanel, 1);
        grid.Children.Add(membersPanel);
        grid.Children.Add(chatPanel);

        return Ui.Card(Ui.Col(connectInfo, connect, _linkStatus, actions, grid), "联动功能");
    }

    private void OnLinkStatus(object? sender, string message) => Dispatcher.BeginInvoke(() =>
    {
        _host.Log.Write(message);
        if (message.StartsWith("连接状态", StringComparison.Ordinal))
        {
            _linkStatus.Text = message;
        }
        else
        {
            _chatLog.AppendText(message + "\r\n");
            _chatLog.ScrollToEnd();
            _linkStatus.Text = _host.Link.IsConnected ? (_host.Link.IsHost ? "连接状态：房主" : "连接状态：已连接") : "连接状态：未连接";
        }
    });

    private void OnChat(object? sender, LinkMessage m) => Dispatcher.BeginInvoke(() =>
    {
        _chatLog.AppendText($"[{m.Nickname}] {m.Message}\r\n");
        _chatLog.ScrollToEnd();
    });

    private void OnMembers(object? sender, IReadOnlyList<string> members) => Dispatcher.BeginInvoke(() =>
    {
        _members.Items.Clear();
        foreach (var m in members)
        {
            _members.Items.Add(m);
        }
    });
}
