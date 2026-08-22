using NovaMacro.Core;

namespace NovaMacro.Tests;

public sealed class InterfaceContractTests
{
    [Fact]
    public void SimpleTestDouble_CanImplementIKeyboardOutput()
    {
        IKeyboardOutput keyboard = new FakeKeyboardOutput();

        keyboard.KeyDown(KeyboardKey.A);
        keyboard.KeyUp(KeyboardKey.A);

        var fake = Assert.IsType<FakeKeyboardOutput>(keyboard);
        Assert.Equal(
            new[] { (KeyboardKey.A, true), (KeyboardKey.A, false) },
            fake.Events);
    }

    [Fact]
    public async Task SimpleTestDouble_CanImplementIDelay()
    {
        IDelay delay = new FakeDelay();
        TimeSpan duration = TimeSpan.FromMilliseconds(150);
        using var cancellationSource = new CancellationTokenSource();

        await delay.WaitAsync(duration, cancellationSource.Token);

        var fake = Assert.IsType<FakeDelay>(delay);
        Assert.Equal(duration, fake.Duration);
        Assert.Equal(cancellationSource.Token, fake.CancellationToken);
    }

    private sealed class FakeKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, bool IsDown)> Events { get; } = new();

        public void KeyDown(KeyboardKey key)
        {
            Events.Add((key, true));
        }

        public void KeyUp(KeyboardKey key)
        {
            Events.Add((key, false));
        }
    }

    private sealed class FakeDelay : IDelay
    {
        public TimeSpan? Duration { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task WaitAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            Duration = duration;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
