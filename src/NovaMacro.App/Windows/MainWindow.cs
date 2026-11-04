using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Settings;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Windows;

/// <summary>主界面 — feature switches, hotkeys and entry points to every other window.</summary>
public sealed class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly TextBlock _status = Ui.Text(string.Empty, dim: true);
    private readonly TextBlock _lock = Ui.Text(string.Empty);
    private readonly Dictionary<FeatureId, (CheckBox Toggle, Button Hotkey)> _rows = new();
    private readonly Stopwatch _stopPress = new();
    private ToolsWindow? _tools;
    private DebugWindow? _debug;
    private CustomMacroWindow? _custom;

    public MainWindow(AppHost host)
    {
        _host = host;
        Title = "NovaMacro · GTA 按键宏";
        Width = 860;
        Height = 720;
        MinWidth = 720;
        MinHeight = 520;
        WindowPositions.Restore(this, host.Settings, "主界面");

        var root = new DockPanel { LastChildFill = true };
        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        header.Children.Add(Ui.Text("NovaMacro", size: 22, weight: FontWeights.Bold));
        var right = Ui.Col(_status, _lock);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Insert(0, right);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(6) };
        foreach (var feature in FeatureCatalog.All.Where(f => f.Group == FeatureGroup.Macro))
        {
            grid.Children.Add(BuildFeatureCard(feature));
        }

        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateStatus();
        timer.Start();
        host.Runtime.LockChanged += (_, _) => Dispatcher.BeginInvoke(UpdateStatus);
        host.Settings.Changed += (_, e) =>
        {
            if (e.Section == Sec.Switches || e.Section.EndsWith("热键", StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(RefreshRows);
            }
        };
        UpdateStatus();
        Closing += OnClosing;
        LocationChanged += (_, _) => WindowPositions.Save(this, host.Settings, "主界面");
    }

    public void OpenTools() => Dispatcher.Invoke(() => Show(ref _tools, () => new ToolsWindow(_host)));

    public void OpenDebug() => Dispatcher.Invoke(() => Show(ref _debug, () => new DebugWindow(_host)));

    public void OpenCustomMacros() => Dispatcher.Invoke(() => Show(ref _custom, () => new CustomMacroWindow(_host)));

    public void OpenSettings(string? section) => Dispatcher.Invoke(() =>
    {
        var w = new SettingsWindow(_host, section) { Owner = this };
        w.Show();
    });

    private static void Show<T>(ref T? window, Func<T> create)
        where T : Window
    {
        if (window is null || !window.IsLoaded)
        {
            window = create();
        }

        window.Show();
        window.Activate();
    }

    private Border BuildFeatureCard(FeatureDefinition feature)
    {
        var toggle = new CheckBox
        {
            Content = Ui.Text(feature.Name, size: 14, weight: FontWeights.SemiBold),
            IsChecked = _host.Settings.IsFeatureOn(feature),
            ToolTip = feature.Help,
        };
        toggle.Checked += (_, _) => _host.Settings.SetFeatureOn(feature, true);
        toggle.Unchecked += (_, _) => _host.Settings.SetFeatureOn(feature, false);

        var hotkey = new Button
        {
            MinWidth = 150,
            ToolTip = feature.HotkeySection is null ? "该功能由固定组合触发" : "点击修改热键，右键恢复默认",
            IsEnabled = feature.HotkeySection is not null,
        };
        hotkey.Content = HotkeyText(feature);
        hotkey.Click += (_, _) =>
        {
            var captured = HotkeyCaptureWindow.Capture(this, _host, "设置热键 - " + feature.Name);
            if (captured is { } h)
            {
                _host.Settings.SetHotkey(feature, h);
            }
        };
        hotkey.MouseRightButtonUp += (_, _) => _host.Settings.SetHotkey(feature, feature.DefaultHotkey);

        var settings = Ui.Btn("设置", () => OpenSettings(feature.SettingsSection), tip: "打开该功能的设置");
        settings.IsEnabled = feature.SettingsSection is not null;

        var dock = new DockPanel();
        var buttons = Ui.Row(hotkey, settings);
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        dock.Children.Add(toggle);

        var help = Ui.Text(feature.Help.Split('\n')[0], dim: true, size: 11);
        var card = Ui.Card(Ui.Col(dock, help));
        card.ToolTip = feature.Help;
        card.MouseRightButtonUp += (_, e) =>
        {
            if (feature.SettingsSection is not null && e.OriginalSource is not Button)
            {
                OpenSettings(feature.SettingsSection);
            }
        };
        _rows[feature.Id] = (toggle, hotkey);
        return card;
    }

    private string HotkeyText(FeatureDefinition feature) => feature.Id switch
    {
        FeatureId.AutoClick => "大写键 + 双击左键",
        FeatureId.WheelSnack => "Tab轮盘 + 零食键",
        FeatureId.WeaponSwapSpeed => _host.Runtime.Get<WeaponSwapSpeedFeature>()?.Chord.ToString() ?? "未设置",
        FeatureId.AdSpeed => "Shift + W + E",
        FeatureId.InstantHover => "大写键 + 角色键",
        FeatureId.SwitchSession => "Esc + 1/2/3",
        FeatureId.EnterAlternative => "Ctrl + 空格",
        _ => _host.Settings.GetHotkey(feature).ToString(),
    };

    private void RefreshRows()
    {
        foreach (var (id, (toggle, hotkey)) in _rows)
        {
            var feature = FeatureCatalog.Get(id);
            toggle.IsChecked = _host.Settings.IsFeatureOn(feature);
            hotkey.Content = HotkeyText(feature);
        }
    }

    private FrameworkElement BuildFooter()
    {
        var stop = new Button { Content = "一键停止", ToolTip = "单击停止按键宏操作，长按锁定/解锁按键宏", Style = (Style)FindResource("AccentButton") };
        stop.PreviewMouseLeftButtonDown += (_, _) => _stopPress.Restart();
        stop.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_stopPress.ElapsedMilliseconds > 800)
            {
                _host.Runtime.ToggleLock();
            }
            else
            {
                _host.Runtime.StopAll();
            }
        };

        var bar = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };
        bar.Children.Add(stop);
        bar.Children.Add(Ui.Btn("小工具", OpenTools));
        bar.Children.Add(Ui.Btn("自定义宏", OpenCustomMacros));
        bar.Children.Add(Ui.Btn("按键宏设置", () => OpenSettings(null)));
        bar.Children.Add(Ui.Btn("调试输出窗口", OpenDebug));
        bar.Children.Add(Ui.Btn("游戏键位", ShowKeyMap, tip: "查看从 GTA 设置读取的键位"));
        bar.Children.Add(Ui.Btn("打开配置文件夹", () => Process.Start(new ProcessStartInfo(_host.DataFolder) { UseShellExecute = true })));

        var hint = Ui.Text("* 鼠标悬停查看提示，右键跳转相应设置。NovaMacro 只模拟键鼠输入与系统网络规则，不读写游戏内存；使用风险自负。", dim: true, size: 11);
        var panel = Ui.Col(bar, hint);
        panel.Margin = new Thickness(6, 0, 6, 8);
        return panel;
    }

    private void ShowKeyMap()
    {
        var game = _host.Game;
        var info = _host.KeyMap.Describe() +
                   $"\r\n\r\n游戏进程PID：{game.ProcessId}\r\n游戏窗口句柄：{game.Handle}\r\n游戏窗口内宽高：{game.ClientBounds.Width} x {game.ClientBounds.Height}";
        MessageBox.Show(this, info, "游戏键位", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateStatus()
    {
        var game = _host.Game;
        var admin = CommandRunner.IsElevated ? "管理员" : "非管理员";
        _status.Text = game.IsRunning
            ? $"GTAV {GtaInstall.DisplayName(game.Edition)} · PID {game.ProcessId} · {admin}"
            : $"游戏未运行 · {admin}";
        _lock.Text = _host.Runtime.IsLocked ? "按键宏已锁定" : "按键宏可正常触发";
        _lock.Foreground = Ui.Res(_host.Runtime.IsLocked ? "Bad" : "Good");
        Title = _host.Runtime.IsLocked ? "NovaMacro · 已锁定" : "NovaMacro · GTA 按键宏";
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (_host.Settings.GetString(Sec.Other, "退出方式") == "最小化到托盘")
        {
            Hide();
            return;
        }

        if (MessageBox.Show(this, "确定退出 NovaMacro？", "退出", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Instance.ExitApp();
        }
    }
}

/// <summary>[窗口位置记忆] — "x|y" per window.</summary>
public static class WindowPositions
{
    public static void Restore(Window window, SettingsStore settings, string key)
    {
        var raw = settings.GetString(Sec.WindowPositions, key, string.Empty).Split('|');
        if (raw.Length == 2 && double.TryParse(raw[0], out var x) && double.TryParse(raw[1], out var y) &&
            x > SystemParameters.VirtualScreenLeft - 50 && y > SystemParameters.VirtualScreenTop - 50 &&
            x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = x;
            window.Top = y;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    public static void Save(Window window, SettingsStore settings, string key)
    {
        if (window.WindowState == WindowState.Normal && !double.IsNaN(window.Left))
        {
            settings.Set(Sec.WindowPositions, key, $"{(int)window.Left}|{(int)window.Top}");
        }
    }
}
