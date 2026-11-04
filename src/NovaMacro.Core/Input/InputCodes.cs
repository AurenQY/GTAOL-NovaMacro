namespace NovaMacro.Core.Input;

/// <summary>
/// Unified input code space. 1..254 are Windows virtual-key codes (mouse buttons use
/// VK_LBUTTON/VK_RBUTTON/VK_MBUTTON/VK_XBUTTON1/VK_XBUTTON2). Mouse wheel and XInput
/// gamepad buttons are mapped above the VK range so every trigger fits in one int.
/// </summary>
public static class InputCodes
{
    public const int None = 0;
    public const int LeftButton = 0x01;
    public const int RightButton = 0x02;
    public const int MiddleButton = 0x04;
    public const int XButton1 = 0x05; // 侧键下 (back)
    public const int XButton2 = 0x06; // 侧键上 (forward)

    public const int Backspace = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Alt = 0x12;
    public const int Pause = 0x13;
    public const int CapsLock = 0x14;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int PrintScreen = 0x2C;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int D0 = 0x30;
    public const int A = 0x41;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int Numpad0 = 0x60;
    public const int Multiply = 0x6A;
    public const int Add = 0x6B;
    public const int Separator = 0x6C;
    public const int Subtract = 0x6D;
    public const int Decimal = 0x6E;
    public const int Divide = 0x6F;
    public const int F1 = 0x70;
    public const int NumLock = 0x90;
    public const int ScrollLock = 0x91;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LAlt = 0xA4;
    public const int RAlt = 0xA5;
    public const int OemSemicolon = 0xBA;
    public const int OemPlus = 0xBB;
    public const int OemComma = 0xBC;
    public const int OemMinus = 0xBD;
    public const int OemPeriod = 0xBE;
    public const int OemSlash = 0xBF;
    public const int OemTilde = 0xC0;
    public const int OemOpenBracket = 0xDB;
    public const int OemBackslash = 0xDC;
    public const int OemCloseBracket = 0xDD;
    public const int OemQuote = 0xDE;

    /// <summary>Numpad Enter (extended Enter).</summary>
    public const int NumpadEnter = 0x100;
    public const int WheelUp = 0x101;
    public const int WheelDown = 0x102;

    /// <summary>XInput button codes: <c>GamepadBase + bit index</c> of XINPUT_GAMEPAD.wButtons.</summary>
    public const int GamepadBase = 0x200;
    public const int PadUp = GamepadBase + 0;
    public const int PadDown = GamepadBase + 1;
    public const int PadLeft = GamepadBase + 2;
    public const int PadRight = GamepadBase + 3;
    public const int PadMenu = GamepadBase + 4;
    public const int PadView = GamepadBase + 5;
    public const int PadLeftThumb = GamepadBase + 6;
    public const int PadRightThumb = GamepadBase + 7;
    public const int PadLb = GamepadBase + 8;
    public const int PadRb = GamepadBase + 9;
    public const int PadA = GamepadBase + 12;
    public const int PadB = GamepadBase + 13;
    public const int PadX = GamepadBase + 14;
    public const int PadY = GamepadBase + 15;

    public static bool IsMouseButton(int code) => code is LeftButton or RightButton or MiddleButton or XButton1 or XButton2;

    public static bool IsWheel(int code) => code is WheelUp or WheelDown;

    public static bool IsGamepad(int code) => code >= GamepadBase && code < GamepadBase + 16;

    public static bool IsKeyboard(int code) => (code > 0 && code < 0x100 && !IsMouseButton(code)) || code == NumpadEnter;

    public static bool IsModifier(int code) => code is Shift or Control or Alt or LShift or RShift or LControl or RControl or LAlt or RAlt or LWin or RWin;

    public static int Letter(char c) => A + (char.ToUpperInvariant(c) - 'A');

    public static int Digit(int d) => D0 + d;

    public static int Function(int n) => F1 + (n - 1);
}
