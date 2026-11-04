using System.Diagnostics;
using System.Runtime.InteropServices;
using NovaMacro.Core.Input;
using NovaMacro.Platform.Native;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Input;

/// <summary>
/// System-wide input observation with the smallest possible cost for the rest of the PC:
/// <list type="bullet">
/// <item>Keyboard: a low-level hook (needed to swallow hotkey keys). The callback only updates an
/// array and runs the (lock-free, allocation-light) handler.</item>
/// <item>Mouse: Raw Input with RIDEV_INPUTSINK. Raw input is delivered asynchronously, so unlike a
/// low-level mouse hook it never delays mouse movement anywhere on the system. Mouse events are
/// observed only and are never swallowed.</item>
/// <item>Gamepad: XInput polling (backs off while no controller is connected).</item>
/// </list>
/// </summary>
public sealed partial class GlobalInputHook : IInputState, IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const ushort RiLeftDown = 0x0001;
    private const ushort RiLeftUp = 0x0002;
    private const ushort RiRightDown = 0x0004;
    private const ushort RiRightUp = 0x0008;
    private const ushort RiMiddleDown = 0x0010;
    private const ushort RiMiddleUp = 0x0020;
    private const ushort RiButton4Down = 0x0040;
    private const ushort RiButton4Up = 0x0080;
    private const ushort RiButton5Down = 0x0100;
    private const ushort RiButton5Up = 0x0200;
    private const ushort RiWheel = 0x0400;

    private static readonly int RawHeaderSize = 8 + (2 * IntPtr.Size);

    private readonly bool[] _down = new bool[0x220];
    private readonly bool[] _swallowed = new bool[0x100];
    private readonly bool[] _suspect = new bool[0x100];
    private readonly HookProc _keyboardProc;
    private readonly WndProc _wndProc;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Thread? _hookThread;
    private Thread? _padThread;
    private uint _hookThreadId;
    private nint _keyboardHook;
    private nint _rawWindow;
    private nint _rawBuffer;
    private volatile bool _running;
    private long _lastPhysicalInput;

    public GlobalInputHook()
    {
        _keyboardProc = KeyboardCallback;
        _wndProc = WindowProc;
    }

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <summary>Handler(code, isDown, isRepeat) → true to swallow (keyboard only). Must be fast.</summary>
    public Func<int, bool, bool, bool>? Handler { get; set; }

    /// <summary>When set and returning true, Win keys are swallowed (释放WIN键).</summary>
    public Func<bool>? BlockWinKey { get; set; }

    public event EventHandler<InputEvent>? InputChanged;

    public long IdleMilliseconds => _clock.ElapsedMilliseconds - Interlocked.Read(ref _lastPhysicalInput);

    public bool IsDown(int code) => code > 0 && code < _down.Length && _down[code];

    public bool IsModifierDown(ModifierMask mask)
    {
        if (mask == ModifierMask.None)
        {
            return false;
        }

        if (mask.HasFlag(ModifierMask.Ctrl) && !(_down[InputCodes.LControl] || _down[InputCodes.RControl] || _down[InputCodes.Control]))
        {
            return false;
        }

        if (mask.HasFlag(ModifierMask.Shift) && !(_down[InputCodes.LShift] || _down[InputCodes.RShift] || _down[InputCodes.Shift]))
        {
            return false;
        }

        return !mask.HasFlag(ModifierMask.Alt) || _down[InputCodes.LAlt] || _down[InputCodes.RAlt] || _down[InputCodes.Alt];
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        var ready = new ManualResetEventSlim();
        _hookThread = new Thread(() =>
        {
            _hookThreadId = Kernel32.GetCurrentThreadId();
            var module = Kernel32.GetModuleHandleW(null);
            _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            CreateRawInputWindow(module);
            ready.Set();
            while (_running && GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                DispatchMessageW(ref msg);
            }

            UnhookWindowsHookEx(_keyboardHook);
            if (_rawWindow != 0)
            {
                DestroyWindow(_rawWindow);
            }
        })
        {
            IsBackground = true,
            Name = "NovaMacro input",
            Priority = ThreadPriority.Highest,
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        ready.Wait(3000);

        _padThread = new Thread(PollGamepad) { IsBackground = true, Name = "NovaMacro XInput", Priority = ThreadPriority.BelowNormal };
        _padThread.Start();
    }

    public void Dispose()
    {
        _running = false;
        if (_hookThreadId != 0)
        {
            PostThreadMessageW(_hookThreadId, WM_QUIT, 0, 0);
        }
    }

    /// <summary>
    /// Clears keyboard keys we believe are held but Windows reports as released for two consecutive
    /// checks (a key-up can be lost, e.g. across the UAC/secure desktop). Keys we swallowed are skipped
    /// because swallowed keys never reach the system key state. Call every ~250 ms.
    /// </summary>
    public void ReconcileStuckKeys()
    {
        for (var code = 8; code < 0x100; code++)
        {
            if (!_down[code] || _swallowed[code] || code is InputCodes.Shift or InputCodes.Control or InputCodes.Alt)
            {
                _suspect[code] = false;
                continue;
            }

            if ((GetAsyncKeyState(code) & 0x8000) != 0)
            {
                _suspect[code] = false;
                continue;
            }

            if (_suspect[code])
            {
                _suspect[code] = false;
                Process(code, false);
            }
            else
            {
                _suspect[code] = true;
            }
        }
    }

    // ---------------- keyboard (low-level hook) ----------------
    private nint KeyboardCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var code = Marshal.ReadInt32(lParam, 0);
            var flags = Marshal.ReadInt32(lParam, 8);
            if ((flags & LLKHF_INJECTED) == 0)
            {
                var msg = (int)wParam;
                var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (code == InputCodes.Enter && (flags & LLKHF_EXTENDED) != 0)
                {
                    code = InputCodes.NumpadEnter;
                }

                var swallow = Process(code, isDown);
                if (code < _swallowed.Length)
                {
                    _swallowed[code] = isDown && (swallow || _swallowed[code]);
                }

                if (swallow)
                {
                    return 1;
                }

                if ((code == InputCodes.LWin || code == InputCodes.RWin) && BlockWinKey?.Invoke() == true)
                {
                    return 1;
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    // ---------------- mouse (raw input) ----------------
    private void CreateRawInputWindow(nint module)
    {
        var className = Marshal.StringToHGlobalUni("NovaMacroRawInput");
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = module,
            lpszClassName = className,
        };
        RegisterClassExW(ref wc);
        _rawWindow = CreateWindowExW(0, className, 0, 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, module, 0);
        _rawBuffer = Marshal.AllocHGlobal(256);
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RidevInputSink, hwndTarget = _rawWindow };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmInput)
        {
            try
            {
                HandleRawInput(lParam);
            }
            catch
            {
                // never let an exception escape into the window procedure
            }
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleRawInput(nint rawHandle)
    {
        uint size = 256;
        if (GetRawInputData(rawHandle, RidInput, _rawBuffer, ref size, (uint)RawHeaderSize) == unchecked((uint)-1))
        {
            return;
        }

        var type = Marshal.ReadInt32(_rawBuffer, 0);
        var device = Marshal.ReadIntPtr(_rawBuffer, 8);
        if (type != 0 /* RIM_TYPEMOUSE */ || device == 0 /* injected by SendInput */)
        {
            return;
        }

        Interlocked.Exchange(ref _lastPhysicalInput, _clock.ElapsedMilliseconds);
        var buttons = (ushort)Marshal.ReadInt16(_rawBuffer, RawHeaderSize + 4);
        if (buttons == 0)
        {
            return;
        }

        if ((buttons & RiLeftDown) != 0)
        {
            Process(InputCodes.LeftButton, true);
        }

        if ((buttons & RiLeftUp) != 0)
        {
            Process(InputCodes.LeftButton, false);
        }

        if ((buttons & RiRightDown) != 0)
        {
            Process(InputCodes.RightButton, true);
        }

        if ((buttons & RiRightUp) != 0)
        {
            Process(InputCodes.RightButton, false);
        }

        if ((buttons & RiMiddleDown) != 0)
        {
            Process(InputCodes.MiddleButton, true);
        }

        if ((buttons & RiMiddleUp) != 0)
        {
            Process(InputCodes.MiddleButton, false);
        }

        if ((buttons & RiButton4Down) != 0)
        {
            Process(InputCodes.XButton1, true);
        }

        if ((buttons & RiButton4Up) != 0)
        {
            Process(InputCodes.XButton1, false);
        }

        if ((buttons & RiButton5Down) != 0)
        {
            Process(InputCodes.XButton2, true);
        }

        if ((buttons & RiButton5Up) != 0)
        {
            Process(InputCodes.XButton2, false);
        }

        if ((buttons & RiWheel) != 0)
        {
            var delta = Marshal.ReadInt16(_rawBuffer, RawHeaderSize + 6);
            var wheel = delta > 0 ? InputCodes.WheelUp : InputCodes.WheelDown;
            Process(wheel, true);
            Process(wheel, false);
        }
    }

    // ---------------- shared ----------------
    private bool Process(int code, bool isDown)
    {
        if (code <= 0 || code >= _down.Length)
        {
            return false;
        }

        var repeat = isDown && _down[code];
        _down[code] = isDown;
        _down[InputCodes.Shift] = _down[InputCodes.LShift] || _down[InputCodes.RShift];
        _down[InputCodes.Control] = _down[InputCodes.LControl] || _down[InputCodes.RControl];
        _down[InputCodes.Alt] = _down[InputCodes.LAlt] || _down[InputCodes.RAlt];
        Interlocked.Exchange(ref _lastPhysicalInput, _clock.ElapsedMilliseconds);
        if (!repeat)
        {
            InputChanged?.Invoke(this, new InputEvent(code, isDown, false, _clock.ElapsedMilliseconds));
        }

        try
        {
            return Handler?.Invoke(code, isDown, repeat) ?? false;
        }
        catch
        {
            return false;
        }
    }

    private void PollGamepad()
    {
        ushort previous = 0;
        while (_running)
        {
            uint result;
            XInput.XINPUT_STATE state;
            try
            {
                result = XInput.GetState(0, out state);
            }
            catch (DllNotFoundException)
            {
                return;
            }
            catch (EntryPointNotFoundException)
            {
                return;
            }

            if (result != 0)
            {
                if (previous != 0)
                {
                    DispatchPad(previous, 0);
                    previous = 0;
                }

                // No controller: probing XInput is relatively expensive, check rarely.
                Thread.Sleep(3000);
                continue;
            }

            var buttons = state.Gamepad.wButtons;
            if (buttons != previous)
            {
                DispatchPad(previous, buttons);
                previous = buttons;
            }

            Thread.Sleep(15);
        }
    }

    private void DispatchPad(ushort before, ushort after)
    {
        for (var bit = 0; bit < 16; bit++)
        {
            var mask = 1 << bit;
            var was = (before & mask) != 0;
            var now = (after & mask) != 0;
            if (was != now)
            {
                Process(InputCodes.GamepadBase + bit, now);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, nint className, nint windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref MSG msg);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);
}
