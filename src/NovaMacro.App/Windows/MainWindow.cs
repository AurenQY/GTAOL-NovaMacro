using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Settings;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Windows;

/// <summary>
/// 主界面 — like the original tool: every switch on one compact, fixed-size screen (no scrolling).
/// Hover a row for help, click the hotkey to change it, right-click a row to open its settings.
/// </summary>
public sealed class MainWindow : ThemedWindow
{
    private const double ColumnWidth = 236;
    private const double RowHeight = 23;

    private static readonly (string Title, FeatureId[] Ids)[][] Columns =
    {
        new[]
        {
            ("武器", new[] { FeatureId.WeaponJam, FeatureId.AutoClick, FeatureId.WheelSnack, FeatureId.WeaponSwapSpeed }),
            ("M 菜单", new[] { FeatureId.BuyAmmo, FeatureId.MenuSnack, FeatureId.VehicleControl, FeatureId.ServiceVehicle }),
        },
        new[]
        {
            ("移动与视角", new[] { FeatureId.QuickPerspective, FeatureId.AdSpeed, FeatureId.SprintToggle, FeatureId.DelayClimb, FeatureId.InstantHover }),
            ("战局与差传", new[] { FeatureId.SwitchSession, FeatureId.MapTeleport, FeatureId.JobTeleport }),
        },
        new[]
        {
            ("抢劫", new[] { FeatureId.CasinoHeist, FeatureId.CayoPerico }),
            ("其他", new[] { FeatureId.PhoneCall, FeatureId.EnterAlternative, FeatureId.Afk, FeatureId.Screenshot, FeatureId.Crosshair }),
        },
    };

    private readonly AppHost _host;
    private readonly TextBlock _game = Status();
    private readonly TextBlock _admin = Status();
    private readonly TextBlock _lock = Status();
    private readonly Dictionary<FeatureId, (CheckBox Toggle, Button Hotkey)> _rows = new();
    private readonly Stopwatch _stopPress = new();
    private ToolsWindow? _tools;
    private DebugWindow? _debug;
    private CustomMacroWindow? _custom;

    public MainWindow(AppHost host)
    {
        _host = host;
        Title = "NovaMacro";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowPositions.Restore(this, host.Settings, "主界面");

        var root = new DockPanel();

        // ---- title strip ----
        var strip = new DockPanel { Margin = new Thickness(10, 6, 10, 2) };
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(_game);
        status.Children.Add(_admin);
        status.Children.Add(_lock);
        DockPanel.SetDock(status, Dock.Right);
        strip.Children.Add(status);
        strip.Children.Add(new TextBlock
        {
            Inlines =
            {
                new System.Windows.Documents.Run("NovaMacro") { FontWeight = FontWeights.Bold, FontSize = 15 },
                new System.Windows.Documents.Run("  2.0") { Foreground = Ui.Res("FgDim"), FontSize = 11 },
            },
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(strip, Dock.Top);
        root.Children.Add(strip);

        // ---- footer ----
        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // ---- switches: three fixed columns ----
        var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 6) };
        foreach (var column in Columns)
        {
            var col = new StackPanel { Width = ColumnWidth, Margin = new Thickness(2, 0, 2, 0) };
            foreach (var (title, ids) in column)
            {
                col.Children.Add(GroupCaption(title));
                foreach (var id in ids)
                {
                    col.Children.Add(BuildRow(FeatureCatalog.Get(id)));
                }
            }

            columns.Children.Add(new Border
            {
                Background = Ui.Res("Panel"),
                BorderBrush = Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4, 2, 4, 4),
                Margin = new Thickness(2),
                Child = col,
            });
        }

        root.Children.Add(columns);
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
        Deactivated += (_, _) => WindowPositions.Save(this, host.Settings, "主界面");
        Closing += (_, _) => WindowPositions.Save(this, host.Settings, "主界面");
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

    private static TextBlock Status() => new() { FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private static FrameworkElement GroupCaption(string title)
    {
        var bar = new Border { Width = 3, Height = 11, CornerRadius = new CornerRadius(1.5), Background = Ui.Res("Accent"), Margin = new Thickness(2, 0, 6, 0) };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Height = 20, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(bar);
        panel.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("FgDim"), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private Border BuildRow(FeatureDefinition feature)
    {
        var toggle = new CheckBox
        {
            Content = new TextBlock { Text = feature.Name, FontSize = 12 },
            IsChecked = _host.Settings.IsFeatureOn(feature),
            Margin = new Thickness(4, 0, 0, 0),
        };
        toggle.Checked += (_, _) => _host.Settings.SetFeatureOn(feature, true);
        toggle.Unchecked += (_, _) => _host.Settings.SetFeatureOn(feature, false);

        var configurable = feature.HotkeySection is not null;
        var hotkey = new Button
        {
            Style = Ui.Style("LinkButton"),
            Content = HotkeyText(feature),
            ToolTip = configurable ? "点击修改热键，右键恢复默认" : "固定组合触发，点击打开相关设置",
            Foreground = configurable ? Ui.Res("AccentHover") : Ui.Res("FgDim"),
        };
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

        var grid = new Grid { Height = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(hotkey, 1);
        grid.Children.Add(toggle);
        grid.Children.Add(hotkey);

        var row = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(3), Child = grid, ToolTip = feature.Help + (feature.SettingsSection is null ? string.Empty : "\n\n右键打开设置") };
        row.MouseEnter += (_, _) => row.Background = Ui.Res("Hover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseRightButtonUp += (_, e) =>
        {
            if (!e.Handled && feature.SettingsSection is not null)
            {
                OpenSettings(feature.SettingsSection);
            }
        };
        _rows[feature.Id] = (toggle, hotkey);
        return row;
    }

    private string HotkeyText(FeatureDefinition feature) => feature.Id switch
    {
        FeatureId.AutoClick => "大写 + 双击左键",
        FeatureId.WheelSnack => "Tab + 零食键",
        FeatureId.WeaponSwapSpeed => _host.Runtime.Get<WeaponSwapSpeedFeature>()?.Chord.ToString() ?? "未设置",
        FeatureId.AdSpeed => "Shift+W+E",
        FeatureId.InstantHover => "大写 + 角色键",
        FeatureId.SwitchSession => "Esc + 1/2/3",
        FeatureId.EnterAlternative => "Ctrl + 空格",
        _ => _host.Settings.GetHotkey(feature).ToString().Replace("键", string.Empty).Replace(" + ", "+"),
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
        var stop = new Button { Content = "一键停止", ToolTip = "单击停止按键宏操作，长按锁定/解锁按键宏", Style = Ui.Style("AccentButton"), MinWidth = 76 };
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

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(stop);
        bar.Children.Add(Ui.Btn("小工具", OpenTools));
        bar.Children.Add(Ui.Btn("自定义宏", OpenCustomMacros));
        bar.Children.Add(Ui.Btn("按键宏设置", () => OpenSettings(null)));
        bar.Children.Add(Ui.Btn("调试窗口", OpenDebug));
        bar.Children.Add(Ui.Btn("游戏键位", ShowKeyMap, tip: "查看从 GTA 设置读取的键位"));
        bar.Children.Add(Ui.Btn("配置文件夹", () => Process.Start(new ProcessStartInfo(_host.DataFolder) { UseShellExecute = true })));

        var hint = new TextBlock
        {
            Text = "* 鼠标悬停查看提示，右键跳转相应设置",
            FontSize = 11,
            Foreground = Ui.Res("FgDim"),
            Margin = new Thickness(4, 2, 0, 0),
        };
        return new Border
        {
            Background = Ui.Res("Panel"),
            BorderBrush = Ui.Res("Border"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new StackPanel { Children = { bar, hint } },
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
        SetStatus(_game, game.IsRunning ? $"● GTAV {GtaInstall.DisplayName(game.Edition)}" : "● 游戏未运行", game.IsRunning ? "Good" : "FgDim");
        SetStatus(_admin, CommandRunner.IsElevated ? "● 管理员" : "● 非管理员", CommandRunner.IsElevated ? "Good" : "Warn");
        SetStatus(_lock, _host.Runtime.IsLocked ? "● 已锁定" : "● 可触发", _host.Runtime.IsLocked ? "Bad" : "Accent");
        Title = _host.Runtime.IsLocked ? "NovaMacro · 已锁定" : "NovaMacro";
    }

    private static void SetStatus(TextBlock block, string text, string brush)
    {
        block.Text = text;
        block.Foreground = Ui.Res(brush);
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
