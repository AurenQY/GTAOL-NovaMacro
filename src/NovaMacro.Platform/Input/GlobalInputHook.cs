using System.Diagnostics;
using System.Runtime.InteropServices;
using NovaMacro.Core.Input;
using NovaMacro.Platform.Native;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Input;

/// <summary>
/// Low-level keyboard + mouse hooks on a dedicated message-loop thread, plus XInput polling.
/// Tracks physical (non-injected) input state and forwards events to a handler that may swallow them.
/// </summary>
public sealed class GlobalInputHook : IInputState, IDisposable
{
    private readonly bool[] _down = new bool[0x220];
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Thread? _hookThread;
    private Thread? _padThread;
    private uint _hookThreadId;
    private nint _keyboardHook;
    private nint _mouseHook;
    private volatile bool _running;
    private long _lastPhysicalInput;

    public GlobalInputHook()
    {
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    /// <summary>Handler(code, isDown, isRepeat) → true to swallow. Called on the hook thread; keep it fast.</summary>
    public Func<int, bool, bool, bool>? Handler { get; set; }

    /// <summary>When set and returning true, Win keys are swallowed (释放WIN键).</summary>
    public Func<bool>? BlockWinKey { get; set; }

    public event EventHandler<InputEvent>? InputChanged;

    public long IdleMilliseconds => _clock.ElapsedMilliseconds - Interlocked.Read(ref _lastPhysicalInput);

    public bool IsDown(int code) => code > 0 && code < _down.Length && _down[code];

    public bool IsModifierDown(ModifierMask mask)
    {
        if (mask.HasFlag(ModifierMask.Ctrl) && !(IsDown(InputCodes.LControl) || IsDown(InputCodes.RControl) || IsDown(InputCodes.Control)))
        {
            return false;
        }

        if (mask.HasFlag(ModifierMask.Shift) && !(IsDown(InputCodes.LShift) || IsDown(InputCodes.RShift) || IsDown(InputCodes.Shift)))
        {
            return false;
        }

        if (mask.HasFlag(ModifierMask.Alt) && !(IsDown(InputCodes.LAlt) || IsDown(InputCodes.RAlt) || IsDown(InputCodes.Alt)))
        {
            return false;
        }

        return mask != ModifierMask.None;
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
            _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);
            ready.Set();
            while (_running && GetMessageW(out _, 0, 0, 0) > 0)
            {
            }

            UnhookWindowsHookEx(_keyboardHook);
            UnhookWindowsHookEx(_mouseHook);
        })
        {
            IsBackground = true,
            Name = "NovaMacro input hook",
            Priority = ThreadPriority.Highest,
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        ready.Wait(3000);

        _padThread = new Thread(PollGamepad) { IsBackground = true, Name = "NovaMacro XInput" };
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

    private nint KeyboardCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var vk = Marshal.ReadInt32(lParam, 0);
            var flags = Marshal.ReadInt32(lParam, 8);
            var injected = (flags & LLKHF_INJECTED) != 0;
            var msg = (int)wParam;
            var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var code = vk;
            if (code == InputCodes.Enter && (flags & LLKHF_EXTENDED) != 0)
            {
                code = InputCodes.NumpadEnter;
            }

            if (!injected && Process(code, isDown))
            {
                return 1;
            }

            if (!injected && (code == InputCodes.LWin || code == InputCodes.RWin) && BlockWinKey?.Invoke() == true)
            {
                return 1;
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private nint MouseCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            // MSLLHOOKSTRUCT: pt (8 bytes), mouseData (4), flags (4). Read without allocating:
            // mouse moves arrive up to thousands of times per second.
            var msg = (int)wParam;
            var flags = Marshal.ReadInt32(lParam, 12);
            var injected = (flags & LLMHF_INJECTED) != 0;
            if (msg == 0x0200)
            {
                if (!injected)
                {
                    _lastPhysicalInput = _clock.ElapsedMilliseconds;
                }

                return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            if (!injected)
            {
                var mouseData = (uint)Marshal.ReadInt32(lParam, 8);
                var xButton = (mouseData >> 16) & 0xFFFF;
                (int Code, bool Down)? evt = msg switch
                {
                    WM_LBUTTONDOWN => (InputCodes.LeftButton, true),
                    WM_LBUTTONUP => (InputCodes.LeftButton, false),
                    WM_RBUTTONDOWN => (InputCodes.RightButton, true),
                    WM_RBUTTONUP => (InputCodes.RightButton, false),
                    WM_MBUTTONDOWN => (InputCodes.MiddleButton, true),
                    WM_MBUTTONUP => (InputCodes.MiddleButton, false),
                    WM_XBUTTONDOWN => (xButton == XBUTTON1 ? InputCodes.XButton1 : InputCodes.XButton2, true),
                    WM_XBUTTONUP => (xButton == XBUTTON1 ? InputCodes.XButton1 : InputCodes.XButton2, false),
                    _ => null,
                };

                if (msg == WM_MOUSEWHEEL)
                {
                    var delta = (short)((mouseData >> 16) & 0xFFFF);
                    var wheel = delta > 0 ? InputCodes.WheelUp : InputCodes.WheelDown;
                    var swallow = Process(wheel, true);
                    swallow |= Process(wheel, false);
                    if (swallow)
                    {
                        return 1;
                    }
                }
                else if (evt is { } e && Process(e.Code, e.Down))
                {
                    return 1;
                }
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private readonly bool[] _suspect = new bool[0x100];

    /// <summary>
    /// Clears keys we believe are held but Windows reports as released for two consecutive checks
    /// (a key-up can be lost, e.g. across the UAC/secure desktop). Call every ~250 ms.
    /// </summary>
    public void ReconcileStuckKeys()
    {
        for (var code = 1; code < 0x100; code++)
        {
            if (!_down[code] || code is InputCodes.Shift or InputCodes.Control or InputCodes.Alt)
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

    private bool Process(int code, bool isDown)
    {
        if (code <= 0 || code >= _down.Length)
        {
            return false;
        }

        var repeat = isDown && _down[code];
        _down[code] = isDown;
        UpdateGenericModifiers();
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

    private void UpdateGenericModifiers()
    {
        _down[InputCodes.Shift] = _down[InputCodes.LShift] || _down[InputCodes.RShift];
        _down[InputCodes.Control] = _down[InputCodes.LControl] || _down[InputCodes.RControl];
        _down[InputCodes.Alt] = _down[InputCodes.LAlt] || _down[InputCodes.RAlt];
    }

    private void PollGamepad()
    {
        ushort previous = 0;
        var available = true;
        while (_running)
        {
            if (!available)
            {
                Thread.Sleep(2000);
                available = true;
            }

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
                available = false;
                if (previous != 0)
                {
                    DispatchPad(previous, 0);
                    previous = 0;
                }

                continue;
            }

            var buttons = state.Gamepad.wButtons;
            if (buttons != previous)
            {
                DispatchPad(previous, buttons);
                previous = buttons;
            }

            Thread.Sleep(10);
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
}
