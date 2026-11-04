using System.Collections.Concurrent;
using System.Text;

namespace NovaMacro.Core.Logging;

/// <summary>
/// In-memory debug log (调试输出窗口) with optional persistence to 调试日志.txt.
/// Writing is cheap for the caller (it may be the input-hook thread): file output is batched on a
/// background timer instead of touching the disk synchronously.
/// </summary>
public sealed class DebugLog : IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly int _capacity;
    private readonly Timer _flushTimer;

    public DebugLog(int capacity = 3000)
    {
        _capacity = capacity;
        _flushTimer = new Timer(_ => Flush(), null, 1000, 1000);
    }

    public event EventHandler<string>? LineAdded;

    public event EventHandler? Cleared;

    public string? SaveFile { get; set; }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_gate)
        {
            _lines.Add(line);
            if (_lines.Count > _capacity)
            {
                _lines.RemoveRange(0, _lines.Count - _capacity);
            }
        }

        if (SaveFile is not null)
        {
            _pending.Enqueue(line);
        }

        LineAdded?.Invoke(this, line);
    }

    /// <summary>Writes queued lines to <see cref="SaveFile"/>.</summary>
    public void Flush()
    {
        var file = SaveFile;
        if (_pending.IsEmpty)
        {
            return;
        }

        var sb = new StringBuilder();
        while (_pending.TryDequeue(out var line))
        {
            sb.Append(line).Append(Environment.NewLine);
        }

        if (file is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
    }
}
