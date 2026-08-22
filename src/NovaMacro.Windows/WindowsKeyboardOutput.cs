using System.ComponentModel;
using System.Runtime.InteropServices;
using NovaMacro.Core;

namespace NovaMacro.Windows;

public sealed class WindowsKeyboardOutput : IKeyboardOutput
{
    public void KeyDown(KeyboardKey key)
    {
        SendKeyboardInput(key, isKeyUp: false);
    }

    public void KeyUp(KeyboardKey key)
    {
        SendKeyboardInput(key, isKeyUp: true);
    }

    private static void SendKeyboardInput(KeyboardKey key, bool isKeyUp)
    {
        ushort virtualKey = ToVirtualKey(key);
        uint flags = isKeyUp ? NativeMethods.KeyEventKeyUp : 0;

        var inputs = new NativeMethods.Input[]
        {
            new()
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = flags
                    }
                }
            }
        };

        uint insertedInputCount = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (insertedInputCount != inputs.Length)
        {
            int errorCode = Marshal.GetLastWin32Error();
            string action = isKeyUp ? "up" : "down";

            throw new Win32Exception(
                errorCode,
                $"SendInput did not insert the requested key-{action} event " +
                $"for '{key}'. Native error code: {errorCode}.");
        }
    }

    private static ushort ToVirtualKey(KeyboardKey key)
    {
        return key switch
        {
            KeyboardKey.A => 0x41,
            KeyboardKey.B => 0x42,
            KeyboardKey.C => 0x43,
            _ => throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "The keyboard key is not supported by the Windows adapter.")
        };
    }
}
