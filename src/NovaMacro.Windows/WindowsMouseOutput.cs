using System.ComponentModel;
using System.Runtime.InteropServices;
using NovaMacro.Core;

namespace NovaMacro.Windows;

public sealed class WindowsMouseOutput : IMouseOutput
{
    private readonly IMouseInputNativeApi _nativeApi;

    public WindowsMouseOutput()
        : this(new Win32MouseInputNativeApi())
    {
    }

    internal WindowsMouseOutput(IMouseInputNativeApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
    }

    public void ButtonDown(MouseButton button)
    {
        SendMouseInput(button, isButtonUp: false);
    }

    public void ButtonUp(MouseButton button)
    {
        SendMouseInput(button, isButtonUp: true);
    }

    private void SendMouseInput(MouseButton button, bool isButtonUp)
    {
        if (button != MouseButton.Left)
        {
            throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                "Only left mouse-button output is supported.");
        }

        uint flags = isButtonUp
            ? NativeMethods.MouseEventLeftUp
            : NativeMethods.MouseEventLeftDown;
        var inputs = new NativeMethods.Input[]
        {
            new()
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput
                    {
                        Flags = flags
                    }
                }
            }
        };

        MouseInputNativeResult result = _nativeApi.SendInput(
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (result.InsertedCount != inputs.Length)
        {
            string action = isButtonUp ? "up" : "down";
            throw new Win32Exception(
                result.ErrorCode,
                $"SendInput did not insert the requested mouse-button-{action} " +
                $"event for '{button}'. Native error code: {result.ErrorCode}.");
        }
    }
}
