using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MouseOutputTests
{
    private const int NativeErrorCode = 12345;

    [Fact]
    public async Task MacroRunner_MouseTapExecutesDownThenUp()
    {
        var mouse = new RecordingMouseOutput();
        var runner = new MacroRunner(
            new NoOpKeyboardOutput(), mouse, new ImmediateDelay());

        MacroRunResult result = await runner.RunAsync(new MacroSequence(
            new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) }));

        Assert.Equal(MacroRunResult.Completed, result);
        Assert.Equal(
            new[] { "Down:Left", "Up:Left" },
            mouse.Calls);
    }

    [Fact]
    public async Task MacroRunner_MouseDownFailureDoesNotAttemptUp()
    {
        var expected = new InvalidOperationException("down failed");
        var mouse = new RecordingMouseOutput(downFailure: expected);
        var runner = new MacroRunner(
            new NoOpKeyboardOutput(), mouse, new ImmediateDelay());

        InvalidOperationException actual = await Assert.ThrowsAsync<
            InvalidOperationException>(() => runner.RunAsync(new MacroSequence(
                new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) })));

        Assert.Same(expected, actual);
        Assert.Equal(new[] { "Down:Left" }, mouse.Calls);
    }

    [Fact]
    public async Task MacroRunner_MouseUpFailurePropagatesWithoutRetry()
    {
        var expected = new InvalidOperationException("up failed");
        var mouse = new RecordingMouseOutput(upFailure: expected);
        var runner = new MacroRunner(
            new NoOpKeyboardOutput(), mouse, new ImmediateDelay());

        InvalidOperationException actual = await Assert.ThrowsAsync<
            InvalidOperationException>(() => runner.RunAsync(new MacroSequence(
                new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) })));

        Assert.Same(expected, actual);
        Assert.Equal(new[] { "Down:Left", "Up:Left" }, mouse.Calls);
    }

    [Fact]
    public async Task CancellationRaisedByDownStillAllowsRequiredUp()
    {
        using var cancellation = new CancellationTokenSource();
        var mouse = new RecordingMouseOutput(onDown: cancellation.Cancel);
        var runner = new MacroRunner(
            new NoOpKeyboardOutput(), mouse, new ImmediateDelay());

        MacroRunResult result = await runner.RunAsync(
            new MacroSequence(
                new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) }),
            cancellation.Token);

        Assert.Equal(MacroRunResult.Completed, result);
        Assert.Equal(new[] { "Down:Left", "Up:Left" }, mouse.Calls);
    }

    [Fact]
    public async Task CancellationBeforeTapProducesNoMouseInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var mouse = new RecordingMouseOutput();
        var runner = new MacroRunner(
            new NoOpKeyboardOutput(), mouse, new ImmediateDelay());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(
                new MacroSequence(
                    new MacroStep[] { new MouseButtonTapStep(MouseButton.Left) }),
                cancellation.Token));

        Assert.Empty(mouse.Calls);
    }

    [Theory]
    [InlineData(false, 0x0002u)]
    [InlineData(true, 0x0004u)]
    public void WindowsMouseOutput_SubmitsExactSingleMousePacket(
        bool isUp,
        uint expectedFlags)
    {
        var native = new FakeMouseInputNativeApi();
        var output = new WindowsMouseOutput(native);

        if (isUp)
        {
            output.ButtonUp(MouseButton.Left);
        }
        else
        {
            output.ButtonDown(MouseButton.Left);
        }

        FakeMouseInputNativeApi.SendInputCall call = Assert.Single(native.Calls);
        Assert.Equal(Marshal.SizeOf<NativeMethods.Input>(), call.InputSize);
        NativeMethods.Input input = Assert.Single(call.Inputs);
        Assert.Equal(NativeMethods.InputMouse, input.Type);
        Assert.Equal(0, input.Data.Mouse.X);
        Assert.Equal(0, input.Data.Mouse.Y);
        Assert.Equal(0u, input.Data.Mouse.MouseData);
        Assert.Equal(expectedFlags, input.Data.Mouse.Flags);
        Assert.Equal(0u, input.Data.Mouse.Time);
        Assert.Equal((nuint)0, input.Data.Mouse.ExtraInfo);
        Assert.Equal((ushort)0, input.Data.Keyboard.VirtualKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsMouseOutput_NativeFailurePreservesErrorWithoutRetry(bool isUp)
    {
        var native = new FakeMouseInputNativeApi
        {
            Result = new MouseInputNativeResult(0, NativeErrorCode)
        };
        var output = new WindowsMouseOutput(native);

        Win32Exception exception = Assert.Throws<Win32Exception>(() =>
        {
            if (isUp) output.ButtonUp(MouseButton.Left);
            else output.ButtonDown(MouseButton.Left);
        });

        Assert.Equal(NativeErrorCode, exception.NativeErrorCode);
        Assert.Single(native.Calls);
    }

    [Theory]
    [InlineData(MouseButton.XButton1)]
    [InlineData(MouseButton.XButton2)]
    [InlineData((MouseButton)99)]
    public void UnsupportedOutputButtonFailsBeforeNativeCall(MouseButton button)
    {
        var native = new FakeMouseInputNativeApi();
        var output = new WindowsMouseOutput(native);

        Assert.Throws<ArgumentOutOfRangeException>(() => output.ButtonDown(button));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void MouseButtonTapStep_RejectsNonLeftButtons()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MouseButtonTapStep(MouseButton.XButton1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MouseButtonTapStep((MouseButton)99));
    }

    private sealed class RecordingMouseOutput : IMouseOutput
    {
        private readonly Exception? _downFailure;
        private readonly Exception? _upFailure;
        private readonly Action? _onDown;
        private readonly ConcurrentQueue<string> _calls = new();

        internal RecordingMouseOutput(
            Exception? downFailure = null,
            Exception? upFailure = null,
            Action? onDown = null)
        {
            _downFailure = downFailure;
            _upFailure = upFailure;
            _onDown = onDown;
        }

        internal IReadOnlyList<string> Calls => _calls.ToArray();

        public void ButtonDown(MouseButton button)
        {
            _calls.Enqueue($"Down:{button}");
            _onDown?.Invoke();
            if (_downFailure is not null) throw _downFailure;
        }

        public void ButtonUp(MouseButton button)
        {
            _calls.Enqueue($"Up:{button}");
            if (_upFailure is not null) throw _upFailure;
        }
    }

    private sealed class NoOpKeyboardOutput : IKeyboardOutput
    {
        public void KeyDown(KeyboardKey key) { }
        public void KeyUp(KeyboardKey key) { }
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
