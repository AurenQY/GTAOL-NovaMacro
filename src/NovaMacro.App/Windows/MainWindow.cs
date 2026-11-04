using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Settings;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Windows;

/// <summary>主界面 — feature switches grouped by category, hotkeys and entry points.</summary>
public sealed class MainWindow : ThemedWindow
{
    private static readonly (string Title, FeatureId[] Ids)[] Groups =
    {
        ("武器", new[] { FeatureId.WeaponJam, FeatureId.AutoClick, FeatureId.WheelSnack, FeatureId.WeaponSwapSpeed }),
        ("M 菜单", new[] { FeatureId.BuyAmmo, FeatureId.MenuSnack, FeatureId.VehicleControl, FeatureId.ServiceVehicle }),
        ("移动与视角", new[] { FeatureId.QuickPerspective, FeatureId.AdSpeed, FeatureId.SprintToggle, FeatureId.DelayClimb, FeatureId.InstantHover }),
        ("战局与差传", new[] { FeatureId.SwitchSession, FeatureId.MapTeleport, FeatureId.JobTeleport }),
        ("抢劫", new[] { FeatureId.CasinoHeist, FeatureId.CayoPerico }),
        ("其他", new[] { FeatureId.PhoneCall, FeatureId.EnterAlternative, FeatureId.Afk, FeatureId.Screenshot, FeatureId.Crosshair }),
    };

    private readonly AppHost _host;
    private readonly (Border Pill, TextBlock Text) _gamePill;
    private readonly (Border Pill, TextBlock Text) _adminPill;
    private readonly (Border Pill, TextBlock Text) _lockPill;
    private readonly Dictionary<FeatureId, (CheckBox Toggle, Button Hotkey)> _rows = new();
    private readonly Stopwatch _stopPress = new();
    private ToolsWindow? _tools;
    private DebugWindow? _debug;
    private CustomMacroWindow? _custom;

    public MainWindow(AppHost host)
    {
        _host = host;
        Title = "NovaMacro";
        Width = 1000;
        Height = 700;
        MinWidth = 760;
        MinHeight = 480;
        WindowPositions.Restore(this, host.Settings, "主界面");

        _gamePill = Pill();
        _adminPill = Pill();
        _lockPill = Pill();

        var root = new DockPanel { LastChildFill = true };

        var header = new DockPanel { Margin = new Thickness(14, 10, 14, 4) };
        var pills = Ui.Row(_gamePill.Pill, _adminPill.Pill, _lockPill.Pill);
        pills.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(pills, Dock.Right);
        header.Children.Add(pills);
        var brand = Ui.Row(
            Ui.Text("NovaMacro", size: 20, weight: FontWeights.Bold),
            Ui.Text("2.0", dim: true, size: 11));
        brand.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(brand);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
        foreach (var (title, ids) in Groups)
        {
            body.Children.Add(Ui.Header(title));
            var grid = new UniformGrid { Columns = 3 };
            foreach (var id in ids)
            {
                grid.Children.Add(BuildFeatureCard(FeatureCatalog.Get(id)));
            }

            body.Children.Add(grid);
        }

        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateStatus();
        timer.Start();
        host.Runtime.LockChanged += (_, _) => Dispatcher.BeginInvoke(UpdateStatus);
        host.Settings.Changed += (_, e) =>
        {
            if (e.Section == Sec.Switches || e.Section == Sec.Other || e.Section.EndsWith("热键", StringComparison.Ordinal))
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
        var w = new SettingsWindow(_host, section);
        if (IsVisible)
        {
            w.Owner = this;
        }

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
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static (Border Pill, TextBlock Text) Pill()
    {
        var text = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(1),
            Child = text,
        };
        return (pill, text);
    }

    private static void SetPill((Border Pill, TextBlock Text) pill, string text, string brushKey)
    {
        var color = ((SolidColorBrush)Ui.Res(brushKey)).Color;
        pill.Text.Text = text;
        pill.Text.Foreground = new SolidColorBrush(color);
        pill.Pill.BorderBrush = new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B));
        pill.Pill.Background = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B));
    }

    private Border BuildFeatureCard(FeatureDefinition feature)
    {
        var toggle = new CheckBox
        {
            Style = Ui.Style("Switch"),
            Content = Ui.Text(feature.Name, size: 13, weight: FontWeights.SemiBold, wrap: false),
            IsChecked = _host.Settings.IsFeatureOn(feature),
        };
        toggle.Checked += (_, _) => _host.Settings.SetFeatureOn(feature, true);
        toggle.Unchecked += (_, _) => _host.Settings.SetFeatureOn(feature, false);

        var configurable = feature.HotkeySection is not null;
        var hotkey = new Button
        {
            Style = Ui.Style("ChipButton"),
            Content = HotkeyText(feature),
            ToolTip = configurable ? "点击修改热键，右键恢复默认" : "固定组合触发（点击打开相关设置）",
        };
        if (!configurable)
        {
            hotkey.Foreground = Ui.Res("FgDim");
        }

        hotkey.Click += (_, _) =>
        {
            if (!configurable)
            {
                if (feature.SettingsSection is not null)
                {
                    OpenSettings(feature.SettingsSection);
                }

                return;
            }

            var captured = HotkeyCaptureWindow.Capture(this, _host, "设置热键 - " + feature.Name);
            if (captured is { } h)
            {
                _host.Settings.SetHotkey(feature, h);
            }
        };
        hotkey.MouseRightButtonUp += (_, e) =>
        {
            if (configurable)
            {
                _host.Settings.SetHotkey(feature, feature.DefaultHotkey);
            }

            e.Handled = true;
        };

        var gear = new Button { Style = Ui.Style("IconButton"), Content = "⚙", ToolTip = "打开该功能的设置", IsEnabled = feature.SettingsSection is not null };
        gear.Click += (_, _) => OpenSettings(feature.SettingsSection);

        var top = new DockPanel();
        var right = Ui.Row(hotkey, gear);
        right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(toggle);

        var help = Ui.Text(feature.Help.Split('\n')[0], dim: true, size: 11, wrap: false);
        help.Margin = new Thickness(40, 1, 2, 0);

        var card = Ui.Card(Ui.Col(top, help));
        card.Margin = new Thickness(3);
        card.Padding = new Thickness(9, 6, 7, 6);
        card.ToolTip = feature.Help;
        card.MouseRightButtonUp += (_, e) =>
        {
            if (!e.Handled && feature.SettingsSection is not null)
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
        FeatureId.WheelSnack => "Tab 轮盘 + 零食键",
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
        var stop = new Button { Content = "■  一键停止", ToolTip = "单击停止按键宏操作，长按锁定/解锁按键宏", Style = Ui.Style("AccentButton"), MinWidth = 110 };
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

        var bar = new WrapPanel();
        bar.Children.Add(stop);
        bar.Children.Add(Ui.Btn("小工具", OpenTools));
        bar.Children.Add(Ui.Btn("自定义宏", OpenCustomMacros));
        bar.Children.Add(Ui.Btn("按键宏设置", () => OpenSettings(null)));
        bar.Children.Add(Ui.Btn("调试输出", OpenDebug));
        bar.Children.Add(Ui.Btn("游戏键位", ShowKeyMap, tip: "查看从 GTA 设置读取的键位"));
        bar.Children.Add(Ui.Btn("配置文件夹", () => Process.Start(new ProcessStartInfo(_host.DataFolder) { UseShellExecute = true })));

        var hint = Ui.Text("鼠标悬停查看说明，右键卡片打开对应设置 · 仅模拟键鼠与系统网络规则，不读写游戏内存", dim: true, size: 11);
        return new Border
        {
            Background = Ui.Res("Panel"),
            BorderBrush = Ui.Res("Border"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Child = Ui.Col(bar, hint),
        };
    }

    private void ShowKeyMap()
    {
        var game = _host.Game;
        var info = _host.KeyMap.Describe() + $"\r\n\r\n游戏窗口内宽高：{game.ClientBounds.Width} x {game.ClientBounds.Height}";
        Dialog.Show(this, info, "游戏键位");
    }

    private void UpdateStatus()
    {
        var game = _host.Game;
        SetPill(_gamePill, game.IsRunning ? $"GTAV {GtaInstall.DisplayName(game.Edition)}" : "游戏未运行", game.IsRunning ? "Good" : "FgDim");
        SetPill(_adminPill, CommandRunner.IsElevated ? "管理员" : "非管理员", CommandRunner.IsElevated ? "Good" : "Warn");
        SetPill(_lockPill, _host.Runtime.IsLocked ? "已锁定" : "可触发", _host.Runtime.IsLocked ? "Bad" : "Accent");
        Title = _host.Runtime.IsLocked ? "NovaMacro · 已锁定" : "NovaMacro";
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (_host.Settings.GetString(Sec.Other, "退出方式") == "最小化到托盘")
        {
            Hide();
            return;
        }

        if (Dialog.Confirm(this, "确定退出 NovaMacro？", "退出"))
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
