using System.ComponentModel;
using System.Runtime.InteropServices;
using NovaMacro.Core;

namespace NovaMacro.Windows;

public sealed class WindowsKeyboardOutput : IKeyboardOutput
{
    private readonly IKeyboardInputNativeApi _nativeApi;

    public WindowsKeyboardOutput()
        : this(new Win32KeyboardInputNativeApi())
    {
    }

    internal WindowsKeyboardOutput(IKeyboardInputNativeApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
    }

    public void KeyDown(KeyboardKey key)
    {
        SendKeyboardInput(key, isKeyUp: false);
    }

    public void KeyUp(KeyboardKey key)
    {
        SendKeyboardInput(key, isKeyUp: true);
    }

    private void SendKeyboardInput(KeyboardKey key, bool isKeyUp)
    {
        KeyboardOutputKeyMapping mapping = KeyboardOutputKeyMapper.Map(key);
        uint flags = mapping.IsExtended
            ? NativeMethods.KeyEventExtendedKey
            : 0;

        if (isKeyUp)
        {
            flags |= NativeMethods.KeyEventKeyUp;
        }

        var inputs = new NativeMethods.Input[]
        {
            new()
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput
                    {
                        VirtualKey = mapping.VirtualKey,
                        Flags = flags
                    }
                }
            }
        };

        KeyboardInputNativeResult result = _nativeApi.SendInput(
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (result.InsertedCount != inputs.Length)
        {
            string action = isKeyUp ? "up" : "down";

            throw new Win32Exception(
                result.ErrorCode,
                $"SendInput did not insert the requested key-{action} event " +
                $"for '{key}'. Native error code: {result.ErrorCode}.");
        }
    }
}
