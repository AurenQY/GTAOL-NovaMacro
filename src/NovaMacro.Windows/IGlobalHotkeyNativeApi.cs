namespace NovaMacro.Windows;

internal readonly record struct GlobalHotkeyNativeMessage(
    uint Id,
    nuint WParam);

internal interface IGlobalHotkeyNativeApi
{
    void EnsureMessageQueue();

    uint GetCurrentThreadId();

    int GetMessage(out GlobalHotkeyNativeMessage message);

    bool PostThreadMessage(
        uint threadId,
        uint message);

    bool RegisterHotKey(
        int registrationId,
        uint modifiers,
        uint virtualKey);

    bool UnregisterHotKey(
        int registrationId);

    short GetAsyncKeyState(
        int virtualKey);
}
