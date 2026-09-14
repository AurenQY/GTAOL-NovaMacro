namespace NovaMacro.Windows;

public sealed class GlobalKeyboardFallbackRegistration : IDisposable
{
    private WindowsGlobalKeyboardFallbackService? _owner;

    internal GlobalKeyboardFallbackRegistration(
        WindowsGlobalKeyboardFallbackService owner,
        KeyboardHotkey hotkey)
    {
        _owner = owner;
        Hotkey = hotkey;
    }

    public KeyboardHotkey Hotkey { get; }

    public void Dispose()
    {
        WindowsGlobalKeyboardFallbackService? owner = Volatile.Read(ref _owner);
        owner?.DisposeRegistration(this);
    }

    internal bool IsOwnedBy(WindowsGlobalKeyboardFallbackService owner)
    {
        return ReferenceEquals(Volatile.Read(ref _owner), owner);
    }

    internal void Invalidate(WindowsGlobalKeyboardFallbackService owner)
    {
        Interlocked.CompareExchange(ref _owner, null, owner);
    }
}
