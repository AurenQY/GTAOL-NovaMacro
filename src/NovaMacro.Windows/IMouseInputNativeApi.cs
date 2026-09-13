namespace NovaMacro.Windows;

internal readonly record struct MouseInputNativeResult(
    uint InsertedCount,
    int ErrorCode);

internal interface IMouseInputNativeApi
{
    MouseInputNativeResult SendInput(
        NativeMethods.Input[] inputs,
        int inputSize);
}
