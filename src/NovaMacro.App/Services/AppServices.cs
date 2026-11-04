using System.Windows;
using NovaMacro.App.Controls;
using System.Windows.Media.Imaging;
using NovaMacro.App.Windows;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Services;

public sealed class AppNotifier : INotifier
{
    private readonly AppHost _host;

    public AppNotifier(AppHost host)
    {
        _host = host;
    }

    public void Log(string message) => _host.Log.Write(message);

    public void Tip(string message)
    {
        _host.Log.Write(message);
        if (_host.Settings.GetBool(Sec.Hud, "启用小工具提示"))
        {
            _host.Overlays.ShowTip(message);
        }
    }

    public void Beep(bool on)
    {
        var s = _host.Settings;
        var prefix = on ? "开启" : "关闭";
        Beeper.Play(s.GetInt(Sec.Tools, prefix + "提示音音频值"), s.GetInt(Sec.Tools, prefix + "提示音持续时间"), s.GetInt(Sec.Device, "蜂鸣提示音量"));
    }

    public void ShowMessage(string title, string message)
    {
        _host.Log.Write($"{title}：{message.Replace("\n", " ")}");
        Application.Current?.Dispatcher.BeginInvoke(() =>
            Dialog.Show(App.Instance.Shell, message, title));
    }
}

public sealed class WpfClipboard : IClipboardService
{
    public void SetImage(PixelBuffer image)
    {
        Run(() =>
        {
            var source = BitmapSource.Create(image.Width, image.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
            Clipboard.SetImage(source);
        });
    }

    public void SetText(string text) => Run(() => Clipboard.SetText(text));

    public string? GetText()
    {
        string? text = null;
        Run(() => text = Clipboard.ContainsText() ? Clipboard.GetText() : null);
        return text;
    }

    private static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        });
    }
}

/// <summary>Owns the HUD tip/timer overlay and the external crosshair overlay.</summary>
public sealed class OverlayService : IOverlayService
{
    private readonly AppHost _host;
    private HudWindow? _hud;
    private CrosshairWindow? _crosshair;

    public OverlayService(AppHost host)
    {
        _host = host;
    }

    public bool CrosshairVisible { get; private set; }

    public bool TimerRunning => _hud?.TimerRunning ?? false;

    public void ShowTip(string message) => Ui(() => EnsureHud().ShowTip(message));

    public void ToggleTimer() => Ui(() => EnsureHud().ToggleTimer());

    public void ToggleCrosshair() => Ui(() =>
    {
        if (CrosshairVisible)
        {
            _crosshair?.Hide();
            CrosshairVisible = false;
            return;
        }

        _crosshair ??= new CrosshairWindow(_host);
        _crosshair.Reposition();
        _crosshair.Show();
        CrosshairVisible = true;
    });

    public void RefreshCrosshair() => Ui(() =>
    {
        if (CrosshairVisible)
        {
            _crosshair?.Redraw();
        }
    });

    private HudWindow EnsureHud()
    {
        return _hud ??= new HudWindow(_host);
    }

    private static void Ui(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
