namespace NovaMacro.Windows;

internal sealed class Win32GlobalHotkeyNativeApi : IGlobalHotkeyNativeApi
{
    public void EnsureMessageQueue()
    {
        NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
    }

    public uint GetCurrentThreadId()
    {
        return NativeMethods.GetCurrentThreadId();
    }

    public int GetMessage(out GlobalHotkeyNativeMessage message)
    {
        int result = NativeMethods.GetMessage(
            out NativeMethods.Message nativeMessage,
            0,
            0,
            0);

        message = new GlobalHotkeyNativeMessage(
            nativeMessage.Id,
            nativeMessage.WParam);

        return result;
    }

    public bool PostThreadMessage(uint threadId, uint message)
    {
        return NativeMethods.PostThreadMessage(threadId, message, 0, 0);
    }

    public bool RegisterHotKey(
        int registrationId,
        uint modifiers,
        uint virtualKey)
    {
        return NativeMethods.RegisterHotKey(
            0,
            registrationId,
            modifiers,
            virtualKey);
    }

    public bool UnregisterHotKey(int registrationId)
    {
        return NativeMethods.UnregisterHotKey(0, registrationId);
    }

    public short GetAsyncKeyState(int virtualKey)
    {
        return NativeMethods.GetAsyncKeyState(virtualKey);
    }
}
