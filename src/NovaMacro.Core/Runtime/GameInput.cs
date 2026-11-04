using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Timing;

namespace NovaMacro.Core.Runtime;

/// <param name="Hold">Time between key down and key up (按下弹起间隔).</param>
/// <param name="After">Time after key up (按键延迟).</param>
public readonly record struct KeyTiming(int Hold, int After)
{
    public static KeyTiming Default => new(25, 25);
}

/// <summary>High-level input helpers driving GTA controls through the user's bindings.</summary>
public sealed class GameInput
{
    private readonly IInputOutput _output;
    private readonly Func<GameKeyMap> _keys;
    private readonly IDelayProvider _delay;

    public GameInput(IInputOutput output, Func<GameKeyMap> keys, IDelayProvider delay)
    {
        _output = output;
        _keys = keys;
        _delay = delay;
    }

    public IInputOutput Output => _output;

    public IDelayProvider Delay => _delay;

    public int Code(GameAction action) => _keys()[action];

    public void Tap(int code, KeyTiming timing, CancellationToken token)
    {
        if (code == InputCodes.None)
        {
            return;
        }

        if (InputCodes.IsWheel(code))
        {
            _output.Wheel(code == InputCodes.WheelUp ? 1 : -1);
            _delay.Wait(timing.After, token);
            return;
        }

        _output.Down(code);
        try
        {
            _delay.Wait(timing.Hold, token);
        }
        finally
        {
            _output.Up(code);
        }

        _delay.Wait(timing.After, token);
    }

    public void Press(GameAction action, KeyTiming timing, CancellationToken token, int times = 1)
    {
        var code = Code(action);
        for (var i = 0; i < times; i++)
        {
            Tap(code, timing, token);
        }
    }

    /// <summary>Moves a menu cursor by a signed number of steps (negative = up/left).</summary>
    public void Navigate(int steps, GameAction positive, GameAction negative, KeyTiming timing, CancellationToken token)
    {
        if (steps > 0)
        {
            Press(positive, timing, token, steps);
        }
        else if (steps < 0)
        {
            Press(negative, timing, token, -steps);
        }
    }

    public void Down(GameAction action) => DownCode(Code(action));

    public void Up(GameAction action) => UpCode(Code(action));

    public void DownCode(int code)
    {
        if (code != InputCodes.None && !InputCodes.IsWheel(code))
        {
            _output.Down(code);
        }
    }

    public void UpCode(int code)
    {
        if (code != InputCodes.None && !InputCodes.IsWheel(code))
        {
            _output.Up(code);
        }
    }

    public void Wait(int ms, CancellationToken token) => _delay.Wait(ms, token);

    public void Click(int x, int y, int button, KeyTiming timing, CancellationToken token)
    {
        _output.MoveTo(x, y);
        _delay.Wait(-15, token);
        Tap(button, timing, token);
    }
}
