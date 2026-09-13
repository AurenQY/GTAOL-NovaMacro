using System.Collections.Concurrent;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal sealed class FakeGlobalMouseButtonNativeApi
    : IGlobalMouseButtonNativeApi,
      IDisposable
{
    internal const uint WmQuit = 0x0012;
    internal const uint CommandMessage = 0x8002;
    internal const uint WmXButtonDown = 0x020B;
    internal const uint WmXButtonUp = 0x020C;

    private readonly BlockingCollection<NativeResult> _messages =
        new(new ConcurrentQueue<NativeResult>());
    private readonly ConcurrentQueue<MouseHookInstallResult> _installResults =
        new();
    private readonly ConcurrentQueue<MouseHookRemovalResult> _removalResults =
        new();
    private readonly ConcurrentQueue<bool> _postResults = new();
    private readonly ConcurrentDictionary<nint, uint> _mouseData = new();
    private int _nextDataToken;
    private bool _disposed;

    internal ConcurrentQueue<(uint ThreadId, uint Message)> PostCalls { get; } =
        new();

    internal ConcurrentQueue<nint> RemovalCalls { get; } = new();

    internal int InstallCallCount { get; private set; }

    internal int CallNextCount { get; private set; }

    internal NativeMethods.LowLevelMouseHookProcedure? HookProcedure { get; private set; }

    public void EnsureMessageQueue()
    {
    }

    public uint GetCurrentThreadId() =>
        unchecked((uint)Environment.CurrentManagedThreadId);

    public int GetMessage(out GlobalMouseNativeMessage message)
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
                : new NativeResult(1, new GlobalMouseNativeMessage(message)));
        }
        return succeeded;
    }

    public MouseHookInstallResult InstallLowLevelMouseHook(
        NativeMethods.LowLevelMouseHookProcedure hookProcedure)
    {
        HookProcedure = hookProcedure;
        InstallCallCount++;
        return _installResults.TryDequeue(out MouseHookInstallResult scripted)
            ? scripted
            : new MouseHookInstallResult((nint)1234, 0);
    }

    public MouseHookRemovalResult RemoveLowLevelMouseHook(nint hookHandle)
    {
        RemovalCalls.Enqueue(hookHandle);
        return _removalResults.TryDequeue(out MouseHookRemovalResult scripted)
            ? scripted
            : new MouseHookRemovalResult(true, 0);
    }

    public nint CallNextMouseHook(
        nint hookHandle,
        int code,
        nuint message,
        nint data)
    {
        CallNextCount++;
        return (nint)5678;
    }

    public uint ReadMouseData(nint data)
    {
        return _mouseData[data];
    }

    internal nint InvokeHook(
        uint message,
        ushort xButton,
        int code = 0)
    {
        NativeMethods.LowLevelMouseHookProcedure hook = HookProcedure ??
            throw new InvalidOperationException("The mouse hook is not installed.");
        nint token = (nint)Interlocked.Increment(ref _nextDataToken);
        _mouseData[token] = (uint)xButton << 16;
        try
        {
            return hook(code, message, token);
        }
        finally
        {
            _mouseData.TryRemove(token, out _);
        }
    }

    internal void FailNextInstall(int errorCode)
    {
        _installResults.Enqueue(new MouseHookInstallResult(0, errorCode));
    }

    internal void FailNextRemoval(int errorCode)
    {
        _removalResults.Enqueue(new MouseHookRemovalResult(false, errorCode));
    }

    internal void FailNextGetMessage()
    {
        _messages.Add(new NativeResult(-1, default));
    }

    internal void EnqueueUnexpectedQuit()
    {
        _messages.Add(new NativeResult(0, default));
    }

    internal void FailNextPost()
    {
        _postResults.Enqueue(false);
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
        GlobalMouseNativeMessage Message);
}
