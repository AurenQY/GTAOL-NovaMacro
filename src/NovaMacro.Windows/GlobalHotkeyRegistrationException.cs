using System.ComponentModel;

namespace NovaMacro.Windows;

public sealed class GlobalHotkeyRegistrationException : Win32Exception
{
    internal GlobalHotkeyRegistrationException(
        KeyboardHotkey hotkey,
        int nativeErrorCode,
        string message)
        : base(nativeErrorCode, message)
    {
        Hotkey = hotkey;
    }

    public KeyboardHotkey Hotkey { get; }
}
