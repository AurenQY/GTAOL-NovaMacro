using NovaMacro.Core;

namespace NovaMacro.Windows;

public sealed class GlobalMouseButtonRegistration : IDisposable
{
    private WindowsGlobalMouseButtonService? _owner;

    internal GlobalMouseButtonRegistration(
        WindowsGlobalMouseButtonService owner,
        MouseButton button)
    {
        _owner = owner;
        Button = button;
    }

    public MouseButton Button { get; }

    public void Dispose()
    {
        WindowsGlobalMouseButtonService? owner = Volatile.Read(ref _owner);
        owner?.DisposeRegistration(this);
    }

    internal bool IsOwnedBy(WindowsGlobalMouseButtonService owner)
    {
        return ReferenceEquals(Volatile.Read(ref _owner), owner);
    }

    internal void Invalidate(WindowsGlobalMouseButtonService owner)
    {
        Interlocked.CompareExchange(ref _owner, null, owner);
    }
}
