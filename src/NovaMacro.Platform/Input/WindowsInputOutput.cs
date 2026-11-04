using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NovaMacro.Core.Input;
using NovaMacro.Platform.Native;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Input;

/// <summary>SendInput based output. Keys are sent as hardware scan codes so games using raw input see them.</summary>
public sealed class WindowsInputOutput : IInputOutput
{
    /// <summary>Marker written into dwExtraInfo of every event NovaMacro injects.</summary>
    public static readonly nint InjectionMarker = unchecked((nint)0x4E4F5641);

    private static readonly HashSet<int> ExtendedKeys = new()
    {
        InputCodes.Insert, InputCodes.Delete, InputCodes.Home, InputCodes.End, InputCodes.PageUp, InputCodes.PageDown,
        InputCodes.Left, InputCodes.Up, InputCodes.Right, InputCodes.Down, InputCodes.RControl, InputCodes.RAlt,
        InputCodes.Divide, InputCodes.NumLock, InputCodes.LWin, InputCodes.RWin, InputCodes.PrintScreen, 0x5D,
    };

    private readonly ConcurrentDictionary<int, byte> _held = new();

    public void Down(int code) => Send(code, down: true);

    public void Up(int code) => Send(code, down: false);

    public void Wheel(int notches)
    {
        var input = MouseInput(MOUSEEVENTF_WHEEL, unchecked((uint)(notches * 120)));
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void MoveTo(int x, int y)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1);
        var vh = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1);
        var input = MouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, 0);
        input.U.mi.dx = (int)Math.Round((x - vx) * 65535.0 / vw);
        input.U.mi.dy = (int)Math.Round((y - vy) * 65535.0 / vh);
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void MoveBy(int dx, int dy)
    {
        var input = MouseInput(MOUSEEVENTF_MOVE, 0);
        input.U.mi.dx = dx;
        input.U.mi.dy = dy;
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void ReleaseAll()
    {
        foreach (var code in _held.Keys.ToArray())
        {
            Send(code, down: false);
        }
    }

    private void Send(int code, bool down)
    {
        if (code == InputCodes.None || InputCodes.IsGamepad(code))
        {
            return;
        }

        if (InputCodes.IsWheel(code))
        {
            if (down)
            {
                Wheel(code == InputCodes.WheelUp ? 1 : -1);
            }

            return;
        }

        INPUT input;
        if (InputCodes.IsMouseButton(code))
        {
            var (flag, data) = code switch
            {
                InputCodes.LeftButton => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
                InputCodes.RightButton => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
                InputCodes.MiddleButton => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
                InputCodes.XButton1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON1),
                _ => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON2),
            };
            input = MouseInput(flag, data);
        }
        else
        {
            var vk = code == InputCodes.NumpadEnter ? InputCodes.Enter : code;
            var scan = (ushort)MapVirtualKeyW((uint)vk, 0 /* MAPVK_VK_TO_VSC */);
            var flags = KEYEVENTF_SCANCODE;
            if (ExtendedKeys.Contains(vk) || code == InputCodes.NumpadEnter)
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            if (!down)
            {
                flags |= KEYEVENTF_KEYUP;
            }

            input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = scan, dwFlags = flags, dwExtraInfo = InjectionMarker } },
            };
        }

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (down)
        {
            _held[code] = 0;
        }
        else
        {
            _held.TryRemove(code, out _);
        }
    }

    private static INPUT MouseInput(uint flags, uint data) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = InjectionMarker } },
    };
}
