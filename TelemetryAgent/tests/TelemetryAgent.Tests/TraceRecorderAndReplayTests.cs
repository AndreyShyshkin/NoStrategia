using TelemetryAgent.Core.Mock;
using TelemetryAgent.Core.Models;
using Xunit;

namespace TelemetryAgent.Tests;

public class TraceRecorderAndReplayTests
{
    [Fact]
    public async Task RecorderAndReplayer_RoundtripStreamCorrectly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"trace_test_{Guid.NewGuid():N}.ndjson");
        try
        {
            await using (var recorder = new TraceRecorder())
            {
                await recorder.StartAsync(tempFile);

                for (int i = 0; i < 50; i++)
                {
                    var payload = new TelemetryPayload
                    {
                        PacketId = i,
                        SpeedKmh = 100f + i,
                        Rpm = 5000 + i * 10,
                        Gear = 3,
                        CarModel = "amg_gt3_evo",
                        Track = "spa"
                    };
                    await recorder.RecordFrameAsync(payload);
                }
                await recorder.StopAsync();
                Assert.Equal(50, recorder.FramesRecorded);
            }

            var replayer = new TraceReplayer();
            int count = await replayer.LoadTraceAsync(tempFile);
            Assert.Equal(50, count);
            Assert.True(replayer.IsConnected);

            for (int i = 0; i < 50; i++)
            {
                bool read = replayer.TryReadNextFrame(out var frame);
                Assert.True(read);
                Assert.NotNull(frame);
                Assert.Equal(i, frame.PacketId);
                Assert.Equal(100f + i, frame.SpeedKmh);
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task BenchmarkGenerator_CreatesValidBenchmarkFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bench_test_{Guid.NewGuid():N}.ndjson");
        try
        {
            string generated = await BenchmarkDatasetGenerator.GenerateBenchmarkNdjsonAsync(tempFile, frameCount: 100);
            Assert.True(File.Exists(generated));

            var replayer = new TraceReplayer();
            int count = await replayer.LoadTraceAsync(generated);
            Assert.Equal(100, count);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
