using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal sealed class FakeKeyboardInputNativeApi : IKeyboardInputNativeApi
{
    internal KeyboardInputNativeResult Result { get; set; } = new(
        InsertedCount: 1,
        ErrorCode: 0);

    internal List<SendInputCall> Calls { get; } = new();

    public KeyboardInputNativeResult SendInput(
        NativeMethods.Input[] inputs,
        int inputSize)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Calls.Add(new SendInputCall((NativeMethods.Input[])inputs.Clone(), inputSize));
        return Result;
    }

    internal sealed record SendInputCall(
        NativeMethods.Input[] Inputs,
        int InputSize);
}
