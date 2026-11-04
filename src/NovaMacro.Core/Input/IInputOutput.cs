namespace NovaMacro.Core.Input;

/// <summary>Synthesizes input (implemented with SendInput on Windows).</summary>
public interface IInputOutput
{
    void Down(int code);

    void Up(int code);

    /// <summary>Scrolls the wheel; positive = up.</summary>
    void Wheel(int notches);

    /// <summary>Moves the cursor to absolute screen coordinates.</summary>
    void MoveTo(int x, int y);

    /// <summary>Moves the cursor relatively (raw mouse delta, used for camera/aim).</summary>
    void MoveBy(int dx, int dy);

    /// <summary>Releases every key/button this output currently holds down.</summary>
    void ReleaseAll();
}

/// <summary>Physical input state as observed by the global hooks.</summary>
public interface IInputState
{
    bool IsDown(int code);

    bool IsModifierDown(ModifierMask mask);

    /// <summary>Milliseconds since the last physical (non-injected) user input.</summary>
    long IdleMilliseconds { get; }

    event EventHandler<InputEvent>? InputChanged;
}

public readonly record struct InputEvent(int Code, bool IsDown, bool Injected, long Timestamp);
