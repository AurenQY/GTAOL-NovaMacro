using System.Text;

namespace NovaMacro.Core.Logging;

/// <summary>In-memory debug log (调试输出窗口) with optional persistence to 调试日志.txt.</summary>
public sealed class DebugLog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();
    private readonly int _capacity;

    public DebugLog(int capacity = 5000)
    {
        _capacity = capacity;
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

            if (SaveFile is { } file)
            {
                try
                {
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        LineAdded?.Invoke(this, line);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
