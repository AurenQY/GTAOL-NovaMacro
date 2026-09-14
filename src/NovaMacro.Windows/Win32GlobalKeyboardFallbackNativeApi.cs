using System.Runtime.InteropServices;

namespace NovaMacro.Windows;

internal sealed class Win32GlobalKeyboardFallbackNativeApi
    : IGlobalKeyboardFallbackNativeApi
{
    public void EnsureMessageQueue()
    {
        NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
    }

    public uint GetCurrentThreadId()
    {
        return NativeMethods.GetCurrentThreadId();
    }

    public int GetMessage(out GlobalKeyboardFallbackNativeMessage message)
    {
        int result = NativeMethods.GetMessage(
            out NativeMethods.Message nativeMessage,
            0,
            0,
            0);
        message = new GlobalKeyboardFallbackNativeMessage(nativeMessage.Id);
        return result;
    }

    public bool PostThreadMessage(uint threadId, uint message)
    {
        return NativeMethods.PostThreadMessage(threadId, message, 0, 0);
    }

    public KeyboardHookInstallResult InstallLowLevelKeyboardHook(
        NativeMethods.LowLevelKeyboardHookProcedure hookProcedure)
    {
        ArgumentNullException.ThrowIfNull(hookProcedure);

        nint moduleHandle = NativeMethods.GetModuleHandle(null);
        if (moduleHandle == 0)
        {
            return new KeyboardHookInstallResult(
                HookHandle: 0,
                ErrorCode: Marshal.GetLastWin32Error());
        }

        nint handle = NativeMethods.SetWindowsKeyboardHookEx(
            NativeMethods.WhKeyboardLowLevel,
            hookProcedure,
            moduleHandle,
            0);
        int errorCode = handle == 0 ? Marshal.GetLastWin32Error() : 0;
        return new KeyboardHookInstallResult(handle, errorCode);
    }

    public KeyboardHookRemovalResult RemoveLowLevelKeyboardHook(nint hookHandle)
    {
        bool succeeded = NativeMethods.UnhookWindowsHookEx(hookHandle);
        int errorCode = succeeded ? 0 : Marshal.GetLastWin32Error();
        return new KeyboardHookRemovalResult(succeeded, errorCode);
    }

    public nint CallNextKeyboardHook(
        nint hookHandle,
        int code,
        nuint message,
        nint data)
    {
        return NativeMethods.CallNextHookEx(hookHandle, code, message, data);
    }

    public KeyboardHookEvent ReadKeyboardEvent(nint data)
    {
        NativeMethods.LowLevelKeyboardHookData nativeEvent =
            Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardHookData>(data);
        return new KeyboardHookEvent(
            nativeEvent.VirtualKey,
            nativeEvent.Flags);
    }
}
