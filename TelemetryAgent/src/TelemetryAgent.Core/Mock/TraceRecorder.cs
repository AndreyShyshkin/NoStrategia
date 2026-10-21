using System.Text.Json;
using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Mock;

/// <summary>
/// Records telemetry streams into standardized Newline-Delimited JSON (NDJSON) format
/// for reproducible offline testing, regression analysis, and machine learning model validation.
/// </summary>
public sealed class TraceRecorder : IAsyncDisposable
{
    private StreamWriter? _writer;
    private readonly object _lock = new();
    private bool _isRecording;
    private long _framesRecorded;

    public bool IsRecording => _isRecording;
    public long FramesRecorded => _framesRecorded;
    public string OutputPath { get; private set; } = string.Empty;

    public async Task StartAsync(string outputPath)
    {
        if (_isRecording) await StopAsync();

        string dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
        _writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
        OutputPath = outputPath;
        _framesRecorded = 0;
        _isRecording = true;
    }

    public async Task RecordFrameAsync(TelemetryPayload payload)
    {
        if (!_isRecording || _writer == null) return;

        string line = JsonSerializer.Serialize(payload);
        lock (_lock)
        {
            _framesRecorded++;
        }
        await _writer.WriteLineAsync(line);
    }

    public async Task StopAsync()
    {
        if (!_isRecording) return;

        _isRecording = false;
        if (_writer != null)
        {
            await _writer.FlushAsync();
            await _writer.DisposeAsync();
            _writer = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
