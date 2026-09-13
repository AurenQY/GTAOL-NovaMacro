using NovaMacro.Core;

namespace NovaMacro.Windows;

public sealed class GlobalMouseButtonRegistrationException : Exception
{
    internal GlobalMouseButtonRegistrationException(
        MouseButton button,
        int nativeErrorCode,
        string message)
        : base(message)
    {
        Button = button;
        NativeErrorCode = nativeErrorCode;
    }

    public MouseButton Button { get; }

    public int NativeErrorCode { get; }
}
