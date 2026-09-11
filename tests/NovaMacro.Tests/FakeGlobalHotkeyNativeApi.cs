using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

internal sealed class FakeGlobalHotkeyNativeApi : IGlobalHotkeyNativeApi, IDisposable
{
    internal const uint WmQuit = 0x0012;
    internal const uint WmHotkey = 0x0312;
    internal const uint CommandMessage = 0x8001;

    internal const int VirtualKeyShift = 0x10;
    internal const int VirtualKeyControl = 0x11;
    internal const int VirtualKeyAlt = 0x12;
    internal const int VirtualKeyA = 0x41;
    internal const int VirtualKeyB = 0x42;
    internal const int VirtualKeyC = 0x43;
    internal const int VirtualKeyF8 = 0x77;

    private const short KeyDownState = unchecked((short)0x8000);

    private readonly object _threadIdGate = new();
    private readonly object _keyStateGate = new();
    private readonly Dictionary<int, uint> _threadIds = new();
    private readonly Dictionary<int, short> _keyStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _keyQuerySignals = new();
    private readonly ConcurrentQueue<int> _registerFailures = new();
    private readonly ConcurrentQueue<bool> _unregisterResults = new();
    private readonly ConcurrentQueue<bool> _postResults = new();
    private readonly ConcurrentQueue<Exception> _keyStateFailures = new();
    private readonly BlockingCollection<NativeResult> _messages =
        new(new ConcurrentQueue<NativeResult>());
    private readonly ManualResetEventSlim _messageProcessingAllowed = new(true);
    private readonly SemaphoreSlim _postSignal = new(0);
    private uint _nextThreadId = 100;
    private bool _disposed;

    internal ConcurrentQueue<RegisterHotkeyCall> RegisterCalls { get; } = new();

    internal ConcurrentQueue<int> UnregisterCalls { get; } = new();

    internal ConcurrentQueue<PostThreadMessageCall> PostCalls { get; } = new();

    public void EnsureMessageQueue()
    {
    }

    public uint GetCurrentThreadId()
    {
        int managedThreadId = Environment.CurrentManagedThreadId;

        lock (_threadIdGate)
        {
            if (!_threadIds.TryGetValue(managedThreadId, out uint nativeThreadId))
            {
                nativeThreadId = _nextThreadId++;
                _threadIds.Add(managedThreadId, nativeThreadId);
            }

            return nativeThreadId;
        }
    }

    public int GetMessage(out GlobalHotkeyNativeMessage message)
    {
        NativeResult result = _messages.Take();
        _messageProcessingAllowed.Wait();
        message = result.Message;
        return result.Result;
    }

    public bool PostThreadMessage(uint threadId, uint message)
    {
        PostCalls.Enqueue(new PostThreadMessageCall(threadId, message));
        _postSignal.Release();

        bool succeeded = !_postResults.TryDequeue(out bool scriptedResult) ||
            scriptedResult;
        if (!succeeded)
        {
            return false;
        }

        _messages.Add(message == WmQuit
            ? new NativeResult(0, default)
            : new NativeResult(
                1,
                new GlobalHotkeyNativeMessage(message, 0)));

        return true;
    }

    public bool RegisterHotKey(
        int registrationId,
        uint modifiers,
        uint virtualKey)
    {
        RegisterCalls.Enqueue(new RegisterHotkeyCall(
            registrationId,
            modifiers,
            virtualKey));

        if (_registerFailures.TryDequeue(out int nativeErrorCode))
        {
            Marshal.SetLastPInvokeError(nativeErrorCode);
            return false;
        }

        return true;
    }

    public bool UnregisterHotKey(int registrationId)
    {
        UnregisterCalls.Enqueue(registrationId);

        return !_unregisterResults.TryDequeue(out bool scriptedResult) ||
            scriptedResult;
    }

    public short GetAsyncKeyState(int virtualKey)
    {
        if (_keyStateFailures.TryDequeue(out Exception? exception))
        {
            throw exception;
        }

        short state;
        lock (_keyStateGate)
        {
            _keyStates.TryGetValue(virtualKey, out state);
        }

        _keyQuerySignals
            .GetOrAdd(virtualKey, _ => new SemaphoreSlim(0))
            .Release();

        return state;
    }

    internal void EnqueueHotkey(int registrationId)
    {
        _messages.Add(new NativeResult(
            1,
            new GlobalHotkeyNativeMessage(
                WmHotkey,
                unchecked((nuint)registrationId))));
    }

    internal void EnqueueQuit()
    {
        _messages.Add(new NativeResult(0, default));
    }

    internal void FailNextGetMessage()
    {
        _messages.Add(new NativeResult(-1, default));
    }

    internal void FailNextRegister(int nativeErrorCode = 0)
    {
        _registerFailures.Enqueue(nativeErrorCode);
    }

    internal void FailNextUnregister()
    {
        _unregisterResults.Enqueue(false);
    }

    internal void FailNextPostThreadMessage()
    {
        _postResults.Enqueue(false);
    }

    internal void FailNextKeyStateQuery(Exception exception)
    {
        _keyStateFailures.Enqueue(exception);
    }

    internal void SetKeyDown(int virtualKey)
    {
        lock (_keyStateGate)
        {
            _keyStates[virtualKey] = KeyDownState;
        }
    }

    internal void SetKeyUp(int virtualKey)
    {
        lock (_keyStateGate)
        {
            _keyStates[virtualKey] = 0;
        }
    }

    internal Task WaitForKeyQueryAsync(int virtualKey)
    {
        return _keyQuerySignals
            .GetOrAdd(virtualKey, _ => new SemaphoreSlim(0))
            .WaitAsync();
    }

    internal Task WaitForPostAsync()
    {
        return _postSignal.WaitAsync();
    }

    internal void PauseMessageProcessing()
    {
        _messageProcessingAllowed.Reset();
    }

    internal void ResumeMessageProcessing()
    {
        _messageProcessingAllowed.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageProcessingAllowed.Set();
        _messages.Add(new NativeResult(0, default));
    }

    internal readonly record struct RegisterHotkeyCall(
        int RegistrationId,
        uint Modifiers,
        uint VirtualKey);

    internal readonly record struct PostThreadMessageCall(
        uint ThreadId,
        uint Message);

    private readonly record struct NativeResult(
        int Result,
        GlobalHotkeyNativeMessage Message);
}
