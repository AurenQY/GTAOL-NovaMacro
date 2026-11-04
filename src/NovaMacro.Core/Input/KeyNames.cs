namespace NovaMacro.Core.Input;

/// <summary>
/// Chinese display names for input codes, following the naming used by the original
/// tool ("A键", "左光标键", "侧键上", "XB_A" …). Names are also accepted when parsing
/// custom macro scripts and settings.
/// </summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Names = new();
    private static readonly Dictionary<string, int> Codes = new(StringComparer.OrdinalIgnoreCase);

    static KeyNames()
    {
        for (var d = 0; d <= 9; d++)
        {
            Add(InputCodes.Digit(d), $"{d}键", $"{d}");
            Add(InputCodes.Numpad0 + d, $"小键盘{d}键", $"Num{d}", $"Numpad{d}");
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            Add(InputCodes.Letter(c), $"{c}键", c.ToString());
        }

        for (var f = 1; f <= 24; f++)
        {
            Add(InputCodes.Function(f), $"F{f}键", $"F{f}");
        }

        Add(InputCodes.LeftButton, "左键", "鼠标左键", "LButton", "Mouse1");
        Add(InputCodes.RightButton, "右键", "鼠标右键", "RButton", "Mouse2");
        Add(InputCodes.MiddleButton, "中键", "鼠标中键", "MButton", "Mouse3");
        Add(InputCodes.XButton2, "侧键上", "XButton2", "Mouse5");
        Add(InputCodes.XButton1, "侧键下", "XButton1", "Mouse4");
        Add(InputCodes.WheelUp, "滚轮向上", "WheelUp");
        Add(InputCodes.WheelDown, "滚轮向下", "WheelDown");

        Add(InputCodes.Backspace, "退格键", "Backspace");
        Add(InputCodes.Tab, "Tab键", "Tab");
        Add(InputCodes.Enter, "回车键", "Enter", "Return");
        Add(InputCodes.NumpadEnter, "Num Enter", "NumEnter");
        Add(InputCodes.Shift, "Shift键", "Shift");
        Add(InputCodes.Control, "Ctrl键", "Ctrl", "Control");
        Add(InputCodes.Alt, "Alt键", "Alt", "Menu");
        Add(InputCodes.Pause, "Pause键", "Pause", "Break");
        Add(InputCodes.CapsLock, "大写键", "Caps Lock键", "CapsLock", "Caps");
        Add(InputCodes.Escape, "Esc键", "Esc", "Escape");
        Add(InputCodes.Space, "空格键", "Space", "SPACE键");
        Add(InputCodes.PageUp, "PageUp键", "PageUp");
        Add(InputCodes.PageDown, "PageDown键", "PageDown");
        Add(InputCodes.End, "End键", "End");
        Add(InputCodes.Home, "Home键", "Home");
        Add(InputCodes.Left, "左光标键", "Left", "←");
        Add(InputCodes.Up, "上光标键", "Up", "↑");
        Add(InputCodes.Right, "右光标键", "Right", "→");
        Add(InputCodes.Down, "下光标键", "Down", "↓");
        Add(InputCodes.PrintScreen, "PrintScreen键", "PrintScreen", "PrtSc");
        Add(InputCodes.Insert, "Ins键", "Insert", "Ins");
        Add(InputCodes.Delete, "Del键", "Delete", "Del");
        Add(InputCodes.LWin, "Win键", "Win", "LWin");
        Add(InputCodes.RWin, "右Win键", "RWin");
        Add(InputCodes.Multiply, "乘号键", "Multiply");
        Add(InputCodes.Add, "加号键", "Add");
        Add(InputCodes.Subtract, "小键盘减号键", "Subtract");
        Add(InputCodes.Decimal, "小键盘圆点键", "Decimal");
        Add(InputCodes.Divide, "小键盘除号键", "Divide");
        Add(InputCodes.NumLock, "NumLock键", "NumLock");
        Add(InputCodes.ScrollLock, "ScrollLock键", "ScrollLock");
        Add(InputCodes.LShift, "左shift键", "LShift", "LSHIFT键");
        Add(InputCodes.RShift, "右shift键", "RShift", "RSHIFT键");
        Add(InputCodes.LControl, "左CTRL键", "LCtrl", "LCONTROL键");
        Add(InputCodes.RControl, "右CTRL键", "RCtrl", "Right Ctrl", "RCONTROL键");
        Add(InputCodes.LAlt, "左Alt键", "LAlt");
        Add(InputCodes.RAlt, "右Alt键", "RAlt", "Right Alt");
        Add(InputCodes.OemSemicolon, "分号键", ";");
        Add(InputCodes.OemPlus, "等号键", "=");
        Add(InputCodes.OemComma, "逗号键", ",");
        Add(InputCodes.OemMinus, "减号键", "-");
        Add(InputCodes.OemPeriod, "圆点键", ".");
        Add(InputCodes.OemSlash, "除号键", "/");
        Add(InputCodes.OemTilde, "反撇号键", "`");
        Add(InputCodes.OemOpenBracket, "左中括号键", "[");
        Add(InputCodes.OemBackslash, "斜杠键", "\\");
        Add(InputCodes.OemCloseBracket, "右中括号键", "]");
        Add(InputCodes.OemQuote, "单引号键", "'");

        Add(InputCodes.PadUp, "XB_↑", "PadUp");
        Add(InputCodes.PadDown, "XB_↓", "PadDown");
        Add(InputCodes.PadLeft, "XB_←", "PadLeft");
        Add(InputCodes.PadRight, "XB_→", "PadRight");
        Add(InputCodes.PadMenu, "XB_Menu", "PadMenu", "XB_Mune");
        Add(InputCodes.PadView, "XB_View", "PadView");
        Add(InputCodes.PadLeftThumb, "XB_LTB", "PadLTB");
        Add(InputCodes.PadRightThumb, "XB_RTB", "PadRTB");
        Add(InputCodes.PadLb, "XB_LB", "PadLB");
        Add(InputCodes.PadRb, "XB_RB", "PadRB");
        Add(InputCodes.PadA, "XB_A", "PadA");
        Add(InputCodes.PadB, "XB_B", "PadB");
        Add(InputCodes.PadX, "XB_X", "PadX");
        Add(InputCodes.PadY, "XB_Y", "PadY");
    }

    /// <summary>Codes offered in pickers (keyboard, mouse, wheel, gamepad).</summary>
    public static IReadOnlyList<int> AllCodes { get; } = Names.Keys.OrderBy(Order).ToArray();

    public static string GetName(int code)
    {
        if (code == InputCodes.None)
        {
            return "无";
        }

        return Names.TryGetValue(code, out var name) ? name : $"VK{code}";
    }

    public static bool TryParse(string? text, out int code)
    {
        code = InputCodes.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (Codes.TryGetValue(trimmed, out code))
        {
            return true;
        }

        if (trimmed.StartsWith("VK", StringComparison.OrdinalIgnoreCase) && int.TryParse(trimmed[2..], out code))
        {
            return true;
        }

        if (int.TryParse(trimmed, out code) && code > 9)
        {
            return true;
        }

        code = InputCodes.None;
        return false;
    }

    private static int Order(int code) => InputCodes.IsMouseButton(code) || InputCodes.IsWheel(code) ? 1000 + code : InputCodes.IsGamepad(code) ? 2000 + code : code;

    private static void Add(int code, string name, params string[] aliases)
    {
        Names.TryAdd(code, name);
        Codes.TryAdd(name, code);
        foreach (var alias in aliases)
        {
            Codes.TryAdd(alias, code);
        }
    }
}
