using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Mock;

public static class BenchmarkDatasetGenerator
{
    /// <summary>
    /// Generates a realistic 60 Hz GT3 dataset (approx. 2000 frames) and saves it to NDJSON.
    /// </summary>
    public static async Task<string> GenerateBenchmarkNdjsonAsync(string targetFilePath, int frameCount = 1800)
    {
        string dir = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var engine = new MockTelemetryEngine();
        await using var recorder = new TraceRecorder();
        await recorder.StartAsync(targetFilePath);

        for (int i = 0; i < frameCount; i++)
        {
            engine.TryReadPhysics(out var phys);
            engine.TryReadGraphics(out var graph);
            engine.TryReadStatic(out var stat);

            var payload = TelemetryMapper.MapToPayload(in phys, in graph, in stat);
            await recorder.RecordFrameAsync(payload);
        }

        await recorder.StopAsync();
        return targetFilePath;
    }
}
