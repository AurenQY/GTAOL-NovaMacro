using System.Collections.Concurrent;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal sealed class FakeGlobalKeyboardFallbackNativeApi
    : IGlobalKeyboardFallbackNativeApi,
      IDisposable
{
    internal const uint WmQuit = 0x0012;
    internal const uint CommandMessage = 0x8003;
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmKeyUp = 0x0101;
    internal const uint WmSysKeyDown = 0x0104;
    internal const uint WmSysKeyUp = 0x0105;

    internal const uint Injected = 0x00000010;
    internal const uint LowerIntegrityInjected = 0x00000002;
    internal const uint AltDown = 0x00000020;

    internal const uint VirtualKeyShift = 0x10;
    internal const uint VirtualKeyControl = 0x11;
    internal const uint VirtualKeyAlt = 0x12;
    internal const uint VirtualKeyLeftWindows = 0x5B;
    internal const uint VirtualKeyLeftShift = 0xA0;
    internal const uint VirtualKeyRightShift = 0xA1;
    internal const uint VirtualKeyLeftControl = 0xA2;
    internal const uint VirtualKeyRightControl = 0xA3;
    internal const uint VirtualKeyLeftAlt = 0xA4;
    internal const uint VirtualKeyRightAlt = 0xA5;
    internal const uint VirtualKeyH = 0x48;
    internal const uint VirtualKeyR = 0x52;

    private readonly BlockingCollection<NativeResult> _messages =
        new(new ConcurrentQueue<NativeResult>());
    private readonly ConcurrentQueue<KeyboardHookInstallResult> _installResults =
        new();
    private readonly ConcurrentQueue<KeyboardHookRemovalResult> _removalResults =
        new();
    private readonly ConcurrentQueue<bool> _postResults = new();
    private readonly ConcurrentDictionary<nint, KeyboardHookEvent> _events = new();
    private int _nextDataToken;
    private bool _disposed;

    internal ConcurrentQueue<(uint ThreadId, uint Message)> PostCalls { get; } =
        new();

    internal ConcurrentQueue<nint> RemovalCalls { get; } = new();

    internal int InstallCallCount { get; private set; }

    internal int CallNextCount { get; private set; }

    internal NativeMethods.LowLevelKeyboardHookProcedure? HookProcedure
    {
        get;
        private set;
    }

    public void EnsureMessageQueue()
    {
    }

    public uint GetCurrentThreadId() =>
        unchecked((uint)Environment.CurrentManagedThreadId);

    public int GetMessage(out GlobalKeyboardFallbackNativeMessage message)
    {
        NativeResult result = _messages.Take();
        message = result.Message;
        return result.Result;
    }

    public bool PostThreadMessage(uint threadId, uint message)
    {
        PostCalls.Enqueue((threadId, message));
        bool succeeded = !_postResults.TryDequeue(out bool scripted) || scripted;
        if (succeeded)
        {
            _messages.Add(message == WmQuit
                ? new NativeResult(0, default)
                : new NativeResult(
                    1,
                    new GlobalKeyboardFallbackNativeMessage(message)));
        }
        return succeeded;
    }

    public KeyboardHookInstallResult InstallLowLevelKeyboardHook(
        NativeMethods.LowLevelKeyboardHookProcedure hookProcedure)
    {
        HookProcedure = hookProcedure;
        InstallCallCount++;
        return _installResults.TryDequeue(out KeyboardHookInstallResult scripted)
            ? scripted
            : new KeyboardHookInstallResult((nint)2468, 0);
    }

    public KeyboardHookRemovalResult RemoveLowLevelKeyboardHook(nint hookHandle)
    {
        RemovalCalls.Enqueue(hookHandle);
        return _removalResults.TryDequeue(out KeyboardHookRemovalResult scripted)
            ? scripted
            : new KeyboardHookRemovalResult(true, 0);
    }

    public nint CallNextKeyboardHook(
        nint hookHandle,
        int code,
        nuint message,
        nint data)
    {
        CallNextCount++;
        return (nint)8642;
    }

    public KeyboardHookEvent ReadKeyboardEvent(nint data)
    {
        return _events[data];
    }

    internal nint InvokeHook(
        uint message,
        uint virtualKey,
        uint flags = 0,
        int code = 0)
    {
        NativeMethods.LowLevelKeyboardHookProcedure hook = HookProcedure ??
            throw new InvalidOperationException(
                "The keyboard fallback hook is not installed.");
        nint token = (nint)Interlocked.Increment(ref _nextDataToken);
        _events[token] = new KeyboardHookEvent(virtualKey, flags);
        try
        {
            return hook(code, message, token);
        }
        finally
        {
            _events.TryRemove(token, out _);
        }
    }

    internal void FailNextInstall(int errorCode)
    {
        _installResults.Enqueue(new KeyboardHookInstallResult(0, errorCode));
    }

    internal void FailNextRemoval(int errorCode)
    {
        _removalResults.Enqueue(new KeyboardHookRemovalResult(false, errorCode));
    }

    internal void FailNextGetMessage()
    {
        _messages.Add(new NativeResult(-1, default));
    }

    internal void EnqueueUnexpectedQuit()
    {
        _messages.Add(new NativeResult(0, default));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messages.Add(new NativeResult(0, default));
    }

    private readonly record struct NativeResult(
        int Result,
        GlobalKeyboardFallbackNativeMessage Message);
}
