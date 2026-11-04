using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using NovaMacro.Core.Timing;

namespace NovaMacro.Platform.SystemTools;

/// <summary>
/// 自适应延时 frame-rate source: counts DXGI Present events of the game process via ETW
/// (requires administrator rights, like the original "StartTraceA").
/// </summary>
public sealed class FpsMonitor : IFrameRateSource, IDisposable
{
    private const string SessionName = "NovaMacro_Frame_Trace_Sample";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

    private readonly Func<int> _processId;
    private readonly object _gate = new();
    private readonly Queue<(DateTime Time, double Fps)> _history = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private int _frames;
    private DateTime _windowStart = DateTime.UtcNow;
    private double _current;

    public FpsMonitor(Func<int> processId)
    {
        _processId = processId;
    }

    public event EventHandler<double>? Updated;

    public double CurrentFps
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public double RecentMinimumFps
    {
        get
        {
            lock (_gate)
            {
                return _history.Count == 0 ? 0 : _history.Min(h => h.Fps);
            }
        }
    }

    public bool IsRunning => _session is not null;

    /// <summary>Starts tracing; returns an error message or null.</summary>
    public string? Start()
    {
        if (_session is not null)
        {
            return null;
        }

        if (TraceEventSession.IsElevated() != true)
        {
            return "StartTraceA失败！原因：无管理员权限！";
        }

        try
        {
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
            _session.Source.Dynamic.All += OnEvent;
            _thread = new Thread(() =>
            {
                try
                {
                    _session?.Source.Process();
                }
                catch (Exception)
                {
                }
            })
            { IsBackground = true, Name = "NovaMacro FPS" };
            _thread.Start();
            return null;
        }
        catch (Exception ex)
        {
            _session?.Dispose();
            _session = null;
            return "StartTraceA失败！" + ex.Message;
        }
    }

    public void Stop()
    {
        _session?.Dispose();
        _session = null;
        lock (_gate)
        {
            _current = 0;
            _history.Clear();
        }
    }

    public void Dispose() => Stop();

    private void OnEvent(TraceEvent e)
    {
        // DXGI Present_Start (event 42) per frame.
        if ((int)e.ID != 42 || e.ProcessID != _processId())
        {
            return;
        }

        double? publish = null;
        lock (_gate)
        {
            _frames++;
            var now = DateTime.UtcNow;
            var elapsed = (now - _windowStart).TotalSeconds;
            if (elapsed >= 0.5)
            {
                _current = _frames / elapsed;
                _frames = 0;
                _windowStart = now;
                _history.Enqueue((now, _current));
                while (_history.Count > 0 && (now - _history.Peek().Time).TotalSeconds > 5)
                {
                    _history.Dequeue();
                }

                publish = _current;
            }
        }

        if (publish is { } fps)
        {
            Updated?.Invoke(this, fps);
        }
    }
}
