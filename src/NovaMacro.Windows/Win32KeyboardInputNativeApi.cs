using System.Runtime.InteropServices;

namespace NovaMacro.Windows;

internal sealed class Win32KeyboardInputNativeApi : IKeyboardInputNativeApi
{
    public KeyboardInputNativeResult SendInput(
        NativeMethods.Input[] inputs,
        int inputSize)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        uint requestedCount = (uint)inputs.Length;
        uint insertedCount = NativeMethods.SendInput(
            requestedCount,
            inputs,
            inputSize);

        int errorCode = insertedCount == requestedCount
            ? 0
            : Marshal.GetLastWin32Error();

        return new KeyboardInputNativeResult(insertedCount, errorCode);
    }
}
