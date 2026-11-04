using System.Windows;
using System.Windows.Controls;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Input;

namespace NovaMacro.App.Windows;

/// <summary>"等待按键" dialog: captures keyboard, mouse or gamepad input through the global hook.</summary>
public sealed class HotkeyCaptureWindow : Window
{
    private readonly AppHost _host;
    private readonly bool _singleKey;
    private readonly TextBlock _preview;

    private HotkeyCaptureWindow(AppHost host, string title, bool singleKey)
    {
        _host = host;
        _singleKey = singleKey;
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _preview = Ui.Text("等待按键…", size: 18);
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            MinWidth = 380,
            Children =
            {
                Ui.Text(singleKey ? "请按下一个按键（键盘 / 鼠标侧键 / 手柄）" : "请按下新的热键组合（可先按住 Ctrl/Shift/Alt 或副键）"),
                Ui.Text("提示：单独的鼠标左键/右键不会被捕获，请配合功能键或副键使用", dim: true, size: 11),
                _preview,
                Ui.Row(
                    Ui.Btn("清除", () => Finish(Hotkey.None)),
                    Ui.Btn("取消", () => { DialogResult = false; })),
            },
        };
        Loaded += (_, _) =>
        {
            _host.CaptureMode = true;
            _host.Hook.InputChanged += OnInput;
        };
        Closed += (_, _) =>
        {
            _host.Hook.InputChanged -= OnInput;
            _host.CaptureMode = false;
        };
    }

    public Hotkey? Result { get; private set; }

    public static Hotkey? Capture(Window owner, AppHost host, string title, bool singleKey = false)
    {
        var w = new HotkeyCaptureWindow(host, title, singleKey) { Owner = owner };
        return w.ShowDialog() == true ? w.Result : null;
    }

    private void OnInput(object? sender, InputEvent e)
    {
        if (!e.IsDown || InputCodes.IsModifier(e.Code))
        {
            return;
        }

        var state = _host.Hook;
        var mods = ModifierMask.None;
        if (state.IsModifierDown(ModifierMask.Ctrl))
        {
            mods |= ModifierMask.Ctrl;
        }

        if (state.IsModifierDown(ModifierMask.Shift))
        {
            mods |= ModifierMask.Shift;
        }

        if (state.IsModifierDown(ModifierMask.Alt))
        {
            mods |= ModifierMask.Alt;
        }

        var secondary = KeyNames.AllCodes.FirstOrDefault(c => c != e.Code && !InputCodes.IsModifier(c) && !InputCodes.IsWheel(c) &&
                                                             c is not InputCodes.LeftButton and not InputCodes.RightButton && state.IsDown(c));
        if (e.Code is InputCodes.LeftButton or InputCodes.RightButton && mods == ModifierMask.None && secondary == 0)
        {
            return;
        }

        var hotkey = _singleKey ? new Hotkey(e.Code) : new Hotkey(e.Code, mods, secondary);
        Dispatcher.BeginInvoke(() => Finish(hotkey));
    }

    private void Finish(Hotkey hotkey)
    {
        if (!IsLoaded)
        {
            return;
        }

        Result = hotkey;
        _preview.Text = hotkey.ToString();
        DialogResult = true;
    }
}
