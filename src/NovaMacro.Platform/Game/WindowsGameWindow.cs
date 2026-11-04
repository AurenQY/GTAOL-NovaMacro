using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NovaMacro.Core.Game;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Runtime;
using NovaMacro.Platform.Native;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Game;

/// <summary>
/// Tracks the GTA V process (GTA5.exe / GTA5_Enhanced.exe) and its main window.
/// Performance rules: property getters never enumerate processes and never block (they are called
/// from the system-wide keyboard hook on every key press); only <see cref="Refresh"/> (1 s timer)
/// does discovery, and it finds the game by window class instead of enumerating all processes.
/// </summary>
public sealed partial class WindowsGameWindow : IGameWindow, IDisposable
{
    private const string GtaWindowClass = "grcWindow";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitTimeout = 0x00000102;

    private readonly object _refreshGate = new();
    private volatile int _pid;
    private long _hwnd;
    private volatile uint _threadId;
    private volatile string? _path;
    private volatile GtaEdition _edition;
    private nint _processHandle;
    private long _lastFullScan = long.MinValue / 2;

    public event EventHandler? GameExited;

    public event EventHandler? GameStarted;

    public bool IsRunning => _pid != 0;

    public bool IsForeground
    {
        get
        {
            var pid = _pid;
            if (pid == 0)
            {
                return false;
            }

            var fg = GetForegroundWindow();
            if (fg == 0)
            {
                return false;
            }

            GetWindowThreadProcessId(fg, out var fgPid);
            return fgPid == (uint)pid;
        }
    }

    public GtaEdition Edition => _edition;

    public int ProcessId => _pid;

    public nint Handle => (nint)Interlocked.Read(ref _hwnd);

    public string? ExecutablePath => _path;

    public ScreenRect ClientBounds
    {
        get
        {
            var hwnd = Handle;
            if (hwnd == 0 || !GetClientRect(hwnd, out var rect))
            {
                return new ScreenRect(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
            }

            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            return new ScreenRect(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    public bool IsWindowedMode
    {
        get
        {
            var hwnd = Handle;
            return hwnd != 0 && (GetWindowLong(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        }
    }

    public bool IsUsKeyboardLayout
    {
        get
        {
            var thread = _threadId;
            if (thread == 0)
            {
                return true;
            }

            var layout = GetKeyboardLayout(thread);
            return ((long)layout & 0xFFFF) == 0x0409;
        }
    }

    public bool Refresh()
    {
        bool wasRunning;
        bool isRunning;
        lock (_refreshGate)
        {
            wasRunning = _pid != 0;
            if (wasRunning && IsAlive())
            {
                var hwnd = Handle;
                if (hwnd == 0 || !IsWindow(hwnd))
                {
                    AttachWindow(FindMainWindow((uint)_pid));
                }

                return true;
            }

            Detach();
            isRunning = Discover();
        }

        if (wasRunning && !isRunning)
        {
            GameExited?.Invoke(this, EventArgs.Empty);
        }
        else if (!wasRunning && isRunning)
        {
            GameStarted?.Invoke(this, EventArgs.Empty);
        }

        return isRunning;
    }

    public void Activate()
    {
        var hwnd = Handle;
        if (hwnd == 0)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        var fg = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var me = Kernel32.GetCurrentThreadId();
        AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(hwnd);
        AttachThreadInput(me, fgThread, false);
    }

    public void Dispose()
    {
        lock (_refreshGate)
        {
            Detach();
        }
    }

    private bool Discover()
    {
        // Cheap: look for the RAGE engine window class.
        var hwnd = FindWindowW(GtaWindowClass, null);
        while (hwnd != 0)
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (TryAttach(pid, hwnd))
            {
                return true;
            }

            hwnd = FindWindowExW(0, hwnd, GtaWindowClass, null);
        }

        // Rare fallback (e.g. window not created yet): full process scan at most every 10 s.
        var now = Environment.TickCount64;
        if (now - _lastFullScan < 10_000)
        {
            return false;
        }

        _lastFullScan = now;
        foreach (var name in GtaInstall.ProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (_pid == 0 && TryAttach((uint)p.Id, 0))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool TryAttach(uint pid, nint hwnd)
    {
        if (pid == 0)
        {
            return false;
        }

        var handle = Kernel32.OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, pid);
        if (handle == 0)
        {
            return false;
        }

        var path = QueryImagePath(handle);
        var edition = GtaInstall.EditionFromExe(path);
        if (edition == GtaEdition.Unknown)
        {
            Kernel32.CloseHandle(handle);
            return false;
        }

        _processHandle = handle;
        _path = path;
        _edition = edition;
        _pid = (int)pid;
        AttachWindow(hwnd != 0 ? hwnd : FindMainWindow(pid));
        return true;
    }

    private void AttachWindow(nint hwnd)
    {
        Interlocked.Exchange(ref _hwnd, hwnd);
        _threadId = hwnd != 0 ? GetWindowThreadProcessId(hwnd, out _) : 0;
    }

    private bool IsAlive() => _processHandle != 0 && WaitForSingleObject(_processHandle, 0) == WaitTimeout;

    private void Detach()
    {
        if (_processHandle != 0)
        {
            Kernel32.CloseHandle(_processHandle);
            _processHandle = 0;
        }

        _pid = 0;
        _threadId = 0;
        _edition = GtaEdition.Unknown;
        Interlocked.Exchange(ref _hwnd, 0);
    }

    private static string? QueryImagePath(nint handle)
    {
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
    }

    private static nint FindMainWindow(uint pid)
    {
        nint best = 0;
        var bestArea = -1L;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd))
            {
                return true;
            }

            if (GetClientRect(hwnd, out var r))
            {
                var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                }
            }

            return true;
        }, 0);
        return best;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, int flags, StringBuilder name, ref int size);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint childAfter, string? className, string? windowName);
}
