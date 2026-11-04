using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Settings;

namespace NovaMacro.App.Windows;

/// <summary>调试输出窗口.</summary>
public sealed class DebugWindow : ThemedWindow
{
    private readonly AppHost _host;
    private readonly TextBox _box = new()
    {
        IsReadOnly = true,
        VerticalContentAlignment = VerticalAlignment.Top,
        Background = Ui.Res("Panel"),
        BorderThickness = new Thickness(0),
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
        FontSize = 12,
    };

    private readonly TextBlock _fps = Ui.Text(string.Empty, dim: true);

    public DebugWindow(AppHost host)
    {
        _host = host;
        Title = "调试输出窗口";
        Width = 760;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var save = new CheckBox { Content = Ui.Text("保存日志"), IsChecked = host.Settings.GetBool(Sec.Other, "保存日志") };
        save.Checked += (_, _) =>
        {
            host.Settings.SetBool(Sec.Other, "保存日志", true);
            host.Log.Write("* 你启用了保存日志，接下来触发部分宏时将会把调试日志保存至配置文件夹！");
        };
        save.Unchecked += (_, _) =>
        {
            host.Settings.SetBool(Sec.Other, "保存日志", false);
            host.Log.Write("* 你取消了保存日志，将不再自动保存日志！");
        };

        var bar = Ui.Row(Ui.Btn("清空日志", () =>
        {
            host.Log.Clear();
            _box.Text = "* 你已清空日志！\r\n";
        }), Ui.Btn("复制全部", () => Clipboard.SetText(_box.Text)), save, _fps);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(_box);
        Content = dock;

        _box.Text = "* 触发部分按键宏时会在此显示状态...\r\n" + string.Join("\r\n", host.Log.Lines) + "\r\n";
        _box.ScrollToEnd();
        host.Log.LineAdded += OnLine;
        host.Fps.Updated += OnFps;
        Closed += (_, _) =>
        {
            host.Log.LineAdded -= OnLine;
            host.Fps.Updated -= OnFps;
        };
    }

    private void OnLine(object? sender, string line) => Dispatcher.BeginInvoke(() =>
    {
        _box.AppendText(line + "\r\n");
        _box.ScrollToEnd();
    });

    private void OnFps(object? sender, double fps) => Dispatcher.BeginInvoke(() =>
        _fps.Text = $"调试窗口 * FPS: {fps:0} | 自适应延时 ms: {_host.Delay.Resolve(1)}");
}
