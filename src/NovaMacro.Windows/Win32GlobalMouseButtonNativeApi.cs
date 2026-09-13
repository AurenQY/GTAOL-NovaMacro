using System.Runtime.InteropServices;

namespace NovaMacro.Windows;

internal sealed class Win32GlobalMouseButtonNativeApi
    : IGlobalMouseButtonNativeApi
{
    public void EnsureMessageQueue()
    {
        NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
    }

    public uint GetCurrentThreadId()
    {
        return NativeMethods.GetCurrentThreadId();
    }

    public int GetMessage(out GlobalMouseNativeMessage message)
    {
        int result = NativeMethods.GetMessage(
            out NativeMethods.Message nativeMessage,
            0,
            0,
            0);
        message = new GlobalMouseNativeMessage(nativeMessage.Id);
        return result;
    }

    public bool PostThreadMessage(uint threadId, uint message)
    {
        return NativeMethods.PostThreadMessage(threadId, message, 0, 0);
    }

    public MouseHookInstallResult InstallLowLevelMouseHook(
        NativeMethods.LowLevelMouseHookProcedure hookProcedure)
    {
        ArgumentNullException.ThrowIfNull(hookProcedure);

        nint moduleHandle = NativeMethods.GetModuleHandle(null);
        if (moduleHandle == 0)
        {
            return new MouseHookInstallResult(
                HookHandle: 0,
                ErrorCode: Marshal.GetLastWin32Error());
        }

        nint handle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLowLevel,
            hookProcedure,
            moduleHandle,
            0);
        int errorCode = handle == 0 ? Marshal.GetLastWin32Error() : 0;
        return new MouseHookInstallResult(handle, errorCode);
    }

    public MouseHookRemovalResult RemoveLowLevelMouseHook(nint hookHandle)
    {
        bool succeeded = NativeMethods.UnhookWindowsHookEx(hookHandle);
        int errorCode = succeeded ? 0 : Marshal.GetLastWin32Error();
        return new MouseHookRemovalResult(succeeded, errorCode);
    }

    public nint CallNextMouseHook(
        nint hookHandle,
        int code,
        nuint message,
        nint data)
    {
        return NativeMethods.CallNextHookEx(hookHandle, code, message, data);
    }

    public uint ReadMouseData(nint data)
    {
        return Marshal.PtrToStructure<NativeMethods.LowLevelMouseHookData>(data)
            .MouseData;
    }
}
