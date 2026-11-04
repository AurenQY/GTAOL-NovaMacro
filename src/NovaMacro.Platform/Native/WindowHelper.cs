using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Native;

public static class WindowHelper
{
    /// <summary>Makes a window click-through, non-activating and hidden from Alt+Tab (overlays).</summary>
    public static void MakeOverlay(nint hwnd)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }
}
