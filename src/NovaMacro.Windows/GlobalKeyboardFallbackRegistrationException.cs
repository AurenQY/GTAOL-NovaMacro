using System.ComponentModel;

namespace NovaMacro.Windows;

public sealed class GlobalKeyboardFallbackRegistrationException : Win32Exception
{
    internal GlobalKeyboardFallbackRegistrationException(
        KeyboardHotkey hotkey,
        int nativeErrorCode,
        string message)
        : base(nativeErrorCode, message)
    {
        Hotkey = hotkey;
    }

    public KeyboardHotkey Hotkey { get; }
}
