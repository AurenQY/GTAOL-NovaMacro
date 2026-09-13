namespace NovaMacro.Windows;

internal readonly record struct GlobalMouseNativeMessage(uint Id);

internal readonly record struct MouseHookInstallResult(
    nint HookHandle,
    int ErrorCode);

internal readonly record struct MouseHookRemovalResult(
    bool Succeeded,
    int ErrorCode);

internal interface IGlobalMouseButtonNativeApi
{
    void EnsureMessageQueue();

    uint GetCurrentThreadId();

    int GetMessage(out GlobalMouseNativeMessage message);

    bool PostThreadMessage(uint threadId, uint message);

    MouseHookInstallResult InstallLowLevelMouseHook(
        NativeMethods.LowLevelMouseHookProcedure hookProcedure);

    MouseHookRemovalResult RemoveLowLevelMouseHook(nint hookHandle);

    nint CallNextMouseHook(
        nint hookHandle,
        int code,
        nuint message,
        nint data);

    uint ReadMouseData(nint data);
}
