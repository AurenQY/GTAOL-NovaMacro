using System.Runtime.InteropServices;
using static NovaMacro.Platform.Native.User32;

namespace NovaMacro.Platform.Native;

public static partial class WindowHelper
{
    private const int DwmwaUseImmersiveDarkModeOld = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>Makes a window click-through, non-activating and hidden from Alt+Tab (overlays).</summary>
    public static void MakeOverlay(nint hwnd)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>
    /// Dark title bar (Windows 10 1809+) and, on Windows 11, caption/border colours matching the app.
    /// Colours are COLORREF (0x00BBGGRR). Failures are ignored on older systems.
    /// </summary>
    public static void UseDarkTitleBar(nint hwnd, int captionColorRef, int textColorRef, int borderColorRef)
    {
        if (hwnd == 0)
        {
            return;
        }

        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref on, sizeof(int));
            }

            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColorRef, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColorRef, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColorRef, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
