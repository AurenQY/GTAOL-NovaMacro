using System.Diagnostics;

namespace NovaMacro.Core.Timing;

/// <summary>Source of the game's current frame rate (ETW present events on Windows).</summary>
public interface IFrameRateSource
{
    /// <summary>Latest measured FPS, or 0 when unknown.</summary>
    double CurrentFps { get; }

    /// <summary>Lowest FPS observed in the recent window, or 0 when unknown.</summary>
    double RecentMinimumFps { get; }
}

public interface IDelayProvider
{
    /// <summary>
    /// Waits for <paramref name="milliseconds"/>. Semantics match the original tool:
    /// &gt;0 = delay (raised to the adaptive minimum when adaptive delay is on),
    /// 0 = no delay, &lt;0 = absolute delay that bypasses adaptive delay.
    /// Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    void Wait(int milliseconds, CancellationToken token);
}

public sealed class DelayProvider : IDelayProvider
{
    private readonly Func<DelayOptions> _options;
    private readonly IFrameRateSource? _frames;

    public DelayProvider(Func<DelayOptions> options, IFrameRateSource? frames = null)
    {
        _options = options;
        _frames = frames;
    }

    public int Resolve(int milliseconds)
    {
        if (milliseconds == 0)
        {
            return 0;
        }

        if (milliseconds < 0)
        {
            return -milliseconds;
        }

        var options = _options();
        if (!options.Adaptive || _frames is null)
        {
            return milliseconds;
        }

        var fps = _frames.RecentMinimumFps > 0 ? _frames.RecentMinimumFps : _frames.CurrentFps;
        if (fps <= 0)
        {
            return milliseconds;
        }

        var frameMs = (int)Math.Ceiling(1000.0 / fps) + options.AdaptiveTolerance;
        return Math.Max(milliseconds, frameMs);
    }

    public void Wait(int milliseconds, CancellationToken token)
    {
        var ms = Resolve(milliseconds);
        token.ThrowIfCancellationRequested();
        if (ms <= 0)
        {
            return;
        }

        if (!_options().HighPrecision)
        {
            if (token.WaitHandle.WaitOne(ms))
            {
                token.ThrowIfCancellationRequested();
            }

            return;
        }

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var remaining = ms - sw.Elapsed.TotalMilliseconds;
            if (remaining <= 0)
            {
                break;
            }

            token.ThrowIfCancellationRequested();
            if (remaining > 3)
            {
                token.WaitHandle.WaitOne((int)(remaining - 2));
            }
            else
            {
                Thread.SpinWait(64);
            }
        }

        token.ThrowIfCancellationRequested();
    }
}

public readonly record struct DelayOptions(bool HighPrecision, bool Adaptive, int AdaptiveTolerance);
