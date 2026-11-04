namespace NovaMacro.Core.Runtime;

public enum PressGesture
{
    Click,
    DoubleClick,
    LongPress,
}

/// <summary>
/// Turns a stream of down/up events into click / double-click / long-press gestures.
/// Pure state machine: feed events with timestamps and poll regularly.
/// </summary>
public sealed class ClickClassifier
{
    private readonly int _doubleClickMs;
    private readonly int _longPressMs;
    private bool _down;
    private long _downAt;
    private long _lastClickUpAt = -1;
    private int _pendingClicks;
    private bool _longFired;

    public ClickClassifier(int doubleClickMs = 280, int longPressMs = 500)
    {
        _doubleClickMs = doubleClickMs;
        _longPressMs = longPressMs;
    }

    public bool IsDown => _down;

    public IEnumerable<PressGesture> Down(long now)
    {
        _down = true;
        _downAt = now;
        _longFired = false;
        return Array.Empty<PressGesture>();
    }

    public IEnumerable<PressGesture> Up(long now)
    {
        if (!_down)
        {
            return Array.Empty<PressGesture>();
        }

        _down = false;
        if (_longFired)
        {
            _pendingClicks = 0;
            return Array.Empty<PressGesture>();
        }

        _pendingClicks++;
        _lastClickUpAt = now;
        if (_pendingClicks >= 2)
        {
            _pendingClicks = 0;
            return new[] { PressGesture.DoubleClick };
        }

        return Array.Empty<PressGesture>();
    }

    public IEnumerable<PressGesture> Poll(long now)
    {
        if (_down && !_longFired && now - _downAt >= _longPressMs)
        {
            _longFired = true;
            _pendingClicks = 0;
            return new[] { PressGesture.LongPress };
        }

        if (!_down && _pendingClicks == 1 && now - _lastClickUpAt >= _doubleClickMs)
        {
            _pendingClicks = 0;
            return new[] { PressGesture.Click };
        }

        return Array.Empty<PressGesture>();
    }
}
