using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NovaMacro.Platform.Native;

namespace NovaMacro.App.Controls;

/// <summary>
/// Base class for every NovaMacro window: dark background/foreground, compact font and a dark
/// title bar. (Implicit Window styles do not apply to derived window classes, so it is set here.)
/// </summary>
public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        Background = Ui.Res("Bg");
        Foreground = Ui.Res("Fg");
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        FontSize = 12;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SourceInitialized += (_, _) =>
            WindowHelper.UseDarkTitleBar(new WindowInteropHelper(this).Handle, 0x00181412, 0x00F0EAE8, 0x003B312C);
    }
}

/// <summary>Dark replacements for MessageBox.</summary>
public static class Dialog
{
    public static void Show(Window? owner, string message, string title = "NovaMacro") => Run(owner, message, title, confirm: false);

    public static bool Confirm(Window? owner, string message, string title = "确定操作？") => Run(owner, message, title, confirm: true);

    private static bool Run(Window? owner, string message, string title, bool confirm)
    {
        var result = false;
        var w = new ThemedWindow
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Topmost = owner is null,
        };
        if (owner is { IsVisible: true })
        {
            w.Owner = owner;
        }

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            MinWidth = 260,
            Margin = new Thickness(0, 0, 0, 14),
            LineHeight = 19,
        };
        var ok = Ui.Btn(confirm ? "确定" : "知道了", () =>
        {
            result = true;
            w.Close();
        }, accent: true);
        ok.MinWidth = 76;
        ok.IsDefault = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        if (confirm)
        {
            var cancel = Ui.Btn("取消", w.Close);
            cancel.MinWidth = 76;
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);
        }

        w.Content = new StackPanel { Margin = new Thickness(18, 16, 18, 12), Children = { text, buttons } };
        w.ShowDialog();
        return result;
    }
}
