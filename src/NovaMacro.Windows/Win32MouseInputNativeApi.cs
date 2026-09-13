using System.Runtime.InteropServices;

namespace NovaMacro.Windows;

internal sealed class Win32MouseInputNativeApi : IMouseInputNativeApi
{
    public MouseInputNativeResult SendInput(
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

        return new MouseInputNativeResult(insertedCount, errorCode);
    }
}
