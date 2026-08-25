namespace NovaMacro.Windows;

internal readonly record struct KeyboardInputNativeResult(
    uint InsertedCount,
    int ErrorCode);

internal interface IKeyboardInputNativeApi
{
    KeyboardInputNativeResult SendInput(
        NativeMethods.Input[] inputs,
        int inputSize);
}
