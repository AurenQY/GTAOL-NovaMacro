using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Settings;
using NovaMacro.Core.Timing;
using NovaMacro.Platform.Native;

namespace NovaMacro.App.Windows;

/// <summary>Transparent click-through overlay base, following the game client area.</summary>
public abstract class OverlayWindow : Window
{
    protected OverlayWindow(AppHost host)
    {
        Host = host;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SourceInitialized += (_, _) => WindowHelper.MakeOverlay(new WindowInteropHelper(this).Handle);
    }

    protected AppHost Host { get; }

    /// <summary>Game client area (or primary screen) in WPF device-independent units.</summary>
    protected Rect TargetBounds()
    {
        var b = Host.Game.IsRunning ? Host.Game.ClientBounds : new ScreenRect(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
        if (!Host.Game.IsRunning)
        {
            scaleX = scaleY = 1.0;
        }

        return new Rect(b.X * scaleX, b.Y * scaleY, b.Width * scaleX, b.Height * scaleY);
    }
}

/// <summary>
/// HUD辅助 — tool tips and the stopwatch timer. The window is only as large as its text and is
/// hidden when idle: a full-screen layered window over the game costs a lot of GPU/CPU time.
/// </summary>
public sealed class HudWindow : OverlayWindow
{
    private readonly StackPanel _stack = new();
    private readonly Border _tip;
    private readonly TextBlock _tipText;
    private readonly Border _timer;
    private readonly TextBlock _timerMain;
    private readonly TextBlock _timerSuffix;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _hide = new();
    private readonly Stopwatch _stopwatch = new();

    public HudWindow(AppHost host)
        : base(host)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        _tipText = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        _tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 18, 20, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 76, 141, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 5, 9, 5),
            Child = _tipText,
            Visibility = Visibility.Collapsed,
        };
        _timerMain = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold };
        _timerSuffix = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Bottom };
        _timer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 18, 20, 24)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _timerMain, _timerSuffix } },
            Visibility = Visibility.Collapsed,
        };
        _stack.Children.Add(_tip);
        _stack.Children.Add(_timer);
        Content = _stack;
        _clock.Tick += (_, _) => UpdateTimer();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            HideTip();
        };
        SizeChanged += (_, _) => Place();
    }

    public bool TimerRunning => _stopwatch.IsRunning;

    public void ShowTip(string message)
    {
        var s = Host.Settings;
        _tipText.Text = message;
        _tipText.FontSize = s.GetInt(Sec.Hud, "提示字号");
        _tip.BeginAnimation(OpacityProperty, null);
        _tip.Opacity = 1;
        _tip.Visibility = Visibility.Visible;
        ShowWindow();
        _hide.Stop();
        if (s.GetString(Sec.Hud, "提示显示方式") != "常亮")
        {
            _hide.Interval = TimeSpan.FromMilliseconds(s.GetInt(Sec.Hud, "提示停留时间"));
            _hide.Start();
        }
    }

    public void ToggleTimer()
    {
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
            _clock.Stop();
            var text = TimerFormat.FormatFlat(_stopwatch.Elapsed, Host.Settings.GetString(Sec.Hud, "计时器时间格式"));
            _stopwatch.Reset();
            _timer.Visibility = Visibility.Collapsed;
            ShowTip("[计时器] 已停止：" + text);
            return;
        }

        _stopwatch.Restart();
        _timer.Visibility = Visibility.Visible;
        UpdateTimer();
        ShowWindow();
        _clock.Start();
    }

    private void HideTip()
    {
        if (Host.Settings.GetString(Sec.Hud, "提示显示方式") == "渐隐渐现")
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => CollapseTip();
            _tip.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            CollapseTip();
        }
    }

    private void CollapseTip()
    {
        if (_hide.IsEnabled)
        {
            return;
        }

        _tip.Visibility = Visibility.Collapsed;
        if (_timer.Visibility != Visibility.Visible)
        {
            Hide();
        }
    }

    private void ShowWindow()
    {
        if (!IsVisible)
        {
            Show();
        }

        Place();
    }

    private void UpdateTimer()
    {
        var s = Host.Settings;
        var limit = s.GetInt(Sec.Hud, "计时器超时时间");
        var (main, suffix) = TimerFormat.Format(_stopwatch.Elapsed, s.GetString(Sec.Hud, "计时器时间格式"));
        var size = s.GetInt(Sec.Hud, "提示字号") * 1.3;
        _timerMain.FontSize = size;
        _timerSuffix.FontSize = size * 0.6;
        _timerMain.Text = main;
        _timerSuffix.Text = suffix;
        _timerMain.Foreground = limit > 0 && _stopwatch.Elapsed.TotalSeconds >= limit ? Brushes.OrangeRed : Brushes.White;
    }

    private void Place()
    {
        if (!IsVisible)
        {
            return;
        }

        var r = TargetBounds();
        const double m = 16;
        var w = ActualWidth;
        var h = ActualHeight;
        var (x, y) = Host.Settings.GetString(Sec.Hud, "小工具提示位置") switch
        {
            "右上角" => (r.Right - w - m, r.Top + m),
            "左下角" => (r.Left + m, r.Bottom - h - m),
            "右下角" => (r.Right - w - m, r.Bottom - h - m),
            "顶部居中" => (r.Left + ((r.Width - w) / 2), r.Top + m),
            _ => (r.Left + m, r.Top + m),
        };
        Left = x;
        Top = y;
    }
}

/// <summary>Crosshair settings parsed from [外置准星设置] 当前准星数据.</summary>
public sealed record CrosshairStyle(int Color, int Opacity, int Shape, int Length, int Thickness, int Gap, int OutlineColor, int OutlineWidth, bool CenterDot, int Rotation)
{
    public static readonly string[] Shapes = { "圆点", "十字", "圆圈", "T字" };

    public static CrosshairStyle Parse(string data)
    {
        var v = data.Split(',').Select(p => int.TryParse(p.Trim(), out var n) ? n : 0).ToList();
        while (v.Count < 10)
        {
            v.Add(0);
        }

        return new CrosshairStyle(v[0], Math.Clamp(v[1] == 0 ? 100 : v[1], 5, 100), Math.Clamp(v[2], 0, 3), Math.Max(1, v[3]), Math.Max(1, v[4]), Math.Max(0, v[5]), v[6], Math.Max(0, v[7]), v[8] != 0, v[9]);
    }

    public string Serialize() => string.Join(",", Color, Opacity, Shape, Length, Thickness, Gap, OutlineColor, OutlineWidth, CenterDot ? 1 : 0, Rotation);

    public static Color ToColor(int colorRef, byte alpha = 255) => System.Windows.Media.Color.FromArgb(alpha, (byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));
}

/// <summary>外置准星 — a click-through crosshair centred on the game client area.</summary>
public sealed class CrosshairWindow : OverlayWindow
{
    private readonly Canvas _canvas = new();

    public CrosshairWindow(AppHost host)
        : base(host)
    {
        Content = _canvas;
        Width = 200;
        Height = 200;
        host.Settings.Changed += (_, e) =>
        {
            if (e.Section == Sec.Crosshair)
            {
                Dispatcher.BeginInvoke(Redraw);
            }
        };
        Loaded += (_, _) => Redraw();
    }

    public void Reposition()
    {
        var r = TargetBounds();
        var (ox, oy) = Offset();
        Left = r.X + (r.Width / 2) - (Width / 2) + ox;
        Top = r.Y + (r.Height / 2) - (Height / 2) + oy;
    }

    public void Redraw()
    {
        Reposition();
        _canvas.Children.Clear();
        var style = CrosshairStyle.Parse(Host.Settings.GetString(Sec.Crosshair, "当前准星数据"));
        var alpha = (byte)(style.Opacity * 255 / 100);
        var fill = new SolidColorBrush(CrosshairStyle.ToColor(style.Color, alpha));
        var outline = new SolidColorBrush(CrosshairStyle.ToColor(style.OutlineColor, alpha));
        var c = Width / 2;
        var snap = Host.Settings.GetBool(Sec.Crosshair, "像素对齐");
        var group = new Canvas { Width = Width, Height = Height, RenderTransform = new RotateTransform(style.Rotation, c, c), SnapsToDevicePixels = snap };

        void Bar(double x, double y, double w, double h)
        {
            var rect = new Rectangle { Width = w, Height = h, Fill = fill, Stroke = style.OutlineWidth > 0 ? outline : null, StrokeThickness = style.OutlineWidth, SnapsToDevicePixels = snap };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            group.Children.Add(rect);
        }

        var t = style.Thickness;
        var len = style.Length;
        var gap = style.Gap;
        switch (style.Shape)
        {
            case 0:
                break;
            case 2:
                var ring = new Ellipse { Width = len * 2, Height = len * 2, Stroke = fill, StrokeThickness = t };
                Canvas.SetLeft(ring, c - len);
                Canvas.SetTop(ring, c - len);
                group.Children.Add(ring);
                break;
            default:
                Bar(c - gap - len, c - (t / 2.0), len, t);
                Bar(c + gap, c - (t / 2.0), len, t);
                Bar(c - (t / 2.0), c + gap, t, len);
                if (style.Shape == 1)
                {
                    Bar(c - (t / 2.0), c - gap - len, t, len);
                }

                break;
        }

        if (style.CenterDot || style.Shape == 0)
        {
            Bar(c - (t / 2.0), c - (t / 2.0), t, t);
        }

        _canvas.Children.Add(group);
    }

    private (double X, double Y) Offset()
    {
        if (!Host.Settings.GetBool(Sec.Crosshair, "启用准星偏移"))
        {
            return (0, 0);
        }

        var parts = Host.Settings.GetString(Sec.Crosshair, "准星中心偏移").Split('|');
        return (parts.Length > 0 && double.TryParse(parts[0], out var x) ? x : 0, parts.Length > 1 && double.TryParse(parts[1], out var y) ? y : 0);
    }
}

/// <summary>Editor for the crosshair data string, shown on the 外置准星设置 page.</summary>
public sealed class CrosshairEditor : StackPanel
{
    public CrosshairEditor(AppHost host)
    {
        var style = CrosshairStyle.Parse(host.Settings.GetString(Sec.Crosshair, "当前准星数据"));
        var grid = Ui.TwoColumns(200);
        var shape = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var s in CrosshairStyle.Shapes)
        {
            shape.Items.Add(s);
        }

        shape.SelectedIndex = style.Shape;
        TextBox Num(int value) => new() { Text = value.ToString(), Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        var color = Num(style.Color);
        var opacity = Num(style.Opacity);
        var length = Num(style.Length);
        var thickness = Num(style.Thickness);
        var gap = Num(style.Gap);
        var outline = Num(style.OutlineColor);
        var outlineWidth = Num(style.OutlineWidth);
        var rotation = Num(style.Rotation);
        var dot = new CheckBox { IsChecked = style.CenterDot };
        Ui.AddRow(grid, Ui.Text("样式"), shape);
        Ui.AddRow(grid, Ui.Text("颜色 (COLORREF)"), color);
        Ui.AddRow(grid, Ui.Text("不透明度 %"), opacity);
        Ui.AddRow(grid, Ui.Text("长度"), length);
        Ui.AddRow(grid, Ui.Text("粗细"), thickness);
        Ui.AddRow(grid, Ui.Text("间隙"), gap);
        Ui.AddRow(grid, Ui.Text("描边颜色"), outline);
        Ui.AddRow(grid, Ui.Text("描边宽度"), outlineWidth);
        Ui.AddRow(grid, Ui.Text("旋转角度"), rotation);
        Ui.AddRow(grid, Ui.Text("中心点"), dot);
        Children.Add(Ui.Card(grid, "准星样式"));
        Children.Add(Ui.Row(
            Ui.Btn("应用准星", () =>
            {
                int P(TextBox b) => int.TryParse(b.Text, out var v) ? v : 0;
                var updated = new CrosshairStyle(P(color), P(opacity), Math.Max(0, shape.SelectedIndex), P(length), P(thickness), P(gap), P(outline), P(outlineWidth), dot.IsChecked == true, P(rotation));
                host.Settings.Set(Sec.Crosshair, "当前准星数据", updated.Serialize());
                host.Overlays.RefreshCrosshair();
            }, accent: true),
            Ui.Btn("显示/隐藏准星", host.Overlays.ToggleCrosshair),
            Ui.Btn("恢复默认准星", () =>
            {
                host.Settings.Set(Sec.Crosshair, "当前准星数据", "65280,89,1,8,3,0,16777215,0,0,0");
                host.Overlays.RefreshCrosshair();
            })));
    }
}
