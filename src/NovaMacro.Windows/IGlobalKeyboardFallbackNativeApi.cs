namespace NovaMacro.Windows;

internal readonly record struct GlobalKeyboardFallbackNativeMessage(uint Id);

internal readonly record struct KeyboardHookEvent(
    uint VirtualKey,
    uint Flags);

internal readonly record struct KeyboardHookInstallResult(
    nint HookHandle,
    int ErrorCode);

internal readonly record struct KeyboardHookRemovalResult(
    bool Succeeded,
    int ErrorCode);

internal interface IGlobalKeyboardFallbackNativeApi
{
    void EnsureMessageQueue();

    uint GetCurrentThreadId();

    int GetMessage(out GlobalKeyboardFallbackNativeMessage message);

    bool PostThreadMessage(uint threadId, uint message);

    KeyboardHookInstallResult InstallLowLevelKeyboardHook(
        NativeMethods.LowLevelKeyboardHookProcedure hookProcedure);

    KeyboardHookRemovalResult RemoveLowLevelKeyboardHook(nint hookHandle);

    nint CallNextKeyboardHook(
        nint hookHandle,
        int code,
        nuint message,
        nint data);

    KeyboardHookEvent ReadKeyboardEvent(nint data);
}
