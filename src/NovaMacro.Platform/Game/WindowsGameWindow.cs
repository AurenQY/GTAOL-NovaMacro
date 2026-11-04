using System.Diagnostics;
using NovaMacro.Core.Game;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Runtime;
using NovaMacro.Platform.Native;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Game;

/// <summary>Tracks the GTA V process (GTA5.exe / GTA5_Enhanced.exe) and its main window.</summary>
public sealed class WindowsGameWindow : IGameWindow
{
    private readonly object _gate = new();
    private int _pid;
    private nint _hwnd;
    private uint _threadId;
    private string? _path;
    private GtaEdition _edition;
    private long _lastScan;
    private nint _lastForeignForeground;

    public event EventHandler? GameExited;

    public event EventHandler? GameStarted;

    public bool IsRunning
    {
        get
        {
            EnsureFresh();
            lock (_gate)
            {
                return _pid != 0;
            }
        }
    }

    public bool IsForeground
    {
        get
        {
            var fg = GetForegroundWindow();
            if (fg == 0)
            {
                return false;
            }

            GetWindowThreadProcessId(fg, out var pid);
            lock (_gate)
            {
                if (_pid != 0 && pid == _pid)
                {
                    return true;
                }
            }

            // Check an unknown foreground process once per window (this runs on every hooked key).
            if (fg == _lastForeignForeground)
            {
                return false;
            }

            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (GtaInstall.ProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    Refresh();
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            _lastForeignForeground = fg;
            return false;
        }
    }

    public GtaEdition Edition
    {
        get
        {
            EnsureFresh();
            lock (_gate)
            {
                return _edition;
            }
        }
    }

    public int ProcessId
    {
        get
        {
            EnsureFresh();
            lock (_gate)
            {
                return _pid;
            }
        }
    }

    public nint Handle
    {
        get
        {
            EnsureFresh();
            lock (_gate)
            {
                return _hwnd;
            }
        }
    }

    public string? ExecutablePath
    {
        get
        {
            EnsureFresh();
            lock (_gate)
            {
                return _path;
            }
        }
    }

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
            uint thread;
            lock (_gate)
            {
                thread = _threadId;
            }

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
        var wasRunning = _pid != 0;
        Process? found = null;
        foreach (var name in GtaInstall.ProcessNames)
        {
            var candidates = Process.GetProcessesByName(name);
            found = candidates.FirstOrDefault();
            foreach (var other in candidates.Skip(1))
            {
                other.Dispose();
            }

            if (found is not null)
            {
                break;
            }
        }

        _lastForeignForeground = 0;
        lock (_gate)
        {
            _lastScan = Environment.TickCount64;
            if (found is null)
            {
                _pid = 0;
                _hwnd = 0;
                _threadId = 0;
                _edition = GtaEdition.Unknown;
            }
            else
            {
                _pid = found.Id;
                _edition = GtaInstall.EditionFromExe(found.ProcessName);
                try
                {
                    _path = found.MainModule?.FileName ?? _path;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                }

                _hwnd = FindMainWindow((uint)found.Id);
                _threadId = _hwnd != 0 ? GetWindowThreadProcessId(_hwnd, out _) : 0;
                found.Dispose();
            }
        }

        var isRunning = _pid != 0;
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

    private void EnsureFresh()
    {
        long last;
        lock (_gate)
        {
            last = _lastScan;
        }

        if (Environment.TickCount64 - last > 1500)
        {
            Refresh();
        }
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
}
