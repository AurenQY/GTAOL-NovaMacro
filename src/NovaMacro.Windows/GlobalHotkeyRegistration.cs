namespace NovaMacro.Windows;

public sealed class GlobalHotkeyRegistration : IDisposable
{
    private WindowsGlobalHotkeyService? _owner;

    internal GlobalHotkeyRegistration(
        WindowsGlobalHotkeyService owner,
        int registrationId,
        KeyboardHotkey hotkey)
    {
        _owner = owner;
        RegistrationId = registrationId;
        Hotkey = hotkey;
    }

    public KeyboardHotkey Hotkey { get; }

    internal int RegistrationId { get; }

    public void Dispose()
    {
        WindowsGlobalHotkeyService? owner = Volatile.Read(ref _owner);
        owner?.DisposeRegistration(this);
    }

    internal bool IsOwnedBy(WindowsGlobalHotkeyService owner)
    {
        return ReferenceEquals(Volatile.Read(ref _owner), owner);
    }

    internal void Invalidate(WindowsGlobalHotkeyService owner)
    {
        Interlocked.CompareExchange(ref _owner, null, owner);
    }
}
