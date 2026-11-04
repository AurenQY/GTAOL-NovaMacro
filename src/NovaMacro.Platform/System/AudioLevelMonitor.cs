using NAudio.CoreAudioApi;
using NAudio.Wave;
using NovaMacro.Core.Runtime;

namespace NovaMacro.Platform.SystemTools;

/// <summary>听声法 — peak level of the default playback device (WASAPI loopback).</summary>
public sealed class AudioLevelMonitor : IAudioLevelMonitor, IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private float _level;

    public float Level => Volatile.Read(ref _level);

    public bool Start()
    {
        if (_capture is not null)
        {
            return true;
        }

        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _capture?.Dispose();
            _capture = null;
            return false;
        }
    }

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        try
        {
            _capture.StopRecording();
        }
        catch (InvalidOperationException)
        {
        }

        _capture.Dispose();
        _capture = null;
        Volatile.Write(ref _level, 0);
    }

    public void Dispose() => Stop();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded == 0)
        {
            return;
        }

        var peak = 0f;
        if (_capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat || _capture.WaveFormat.BitsPerSample == 32)
        {
            for (var i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(e.Buffer, i)));
            }
        }
        else
        {
            for (var i = 0; i + 2 <= e.BytesRecorded; i += 2)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
            }
        }

        Volatile.Write(ref _level, Math.Min(1f, peak));
    }
}
