using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal sealed class FakeMouseInputNativeApi : IMouseInputNativeApi
{
    internal MouseInputNativeResult Result { get; set; } = new(1, 0);

    internal List<SendInputCall> Calls { get; } = new();

    public MouseInputNativeResult SendInput(
        NativeMethods.Input[] inputs,
        int inputSize)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Calls.Add(new SendInputCall(
            (NativeMethods.Input[])inputs.Clone(),
            inputSize));
        return Result;
    }

    internal sealed record SendInputCall(
        NativeMethods.Input[] Inputs,
        int InputSize);
}
