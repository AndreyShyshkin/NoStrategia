using TelemetryAgent.Core.Mock;
using Xunit;

namespace TelemetryAgent.Tests;

public class MockTelemetryTests
{
    [Fact]
    public void MockEngine_GeneratesRealisticGt3Physics()
    {
        var engine = new MockTelemetryEngine();
        Assert.True(engine.IsConnected);

        bool readPhys = engine.TryReadPhysics(out var phys);
        bool readGraph = engine.TryReadGraphics(out var graph);
        bool readStat = engine.TryReadStatic(out var stat);

        Assert.True(readPhys);
        Assert.True(readGraph);
        Assert.True(readStat);

        Assert.Equal("amg_gt3_evo", stat.CarModel);
        Assert.Equal("spa", stat.Track);
        Assert.True(phys.SpeedKmh > 0, "Speed should be positive");
        Assert.InRange(phys.Rpm, 4000, 7500);
        Assert.InRange(phys.WheelsPressure[0], 25.0f, 32.0f);
        Assert.InRange(phys.TyreCoreTemperature[0], 70.0f, 110.0f);
        Assert.Equal(2, graph.Status); // AC_LIVE
    }

    [Fact]
    public void MockEngine_ProgressesThroughTrackSectorsAndLaps()
    {
        var engine = new MockTelemetryEngine();

        // Simulate 500 ticks (~8.3 seconds)
        for (int i = 0; i < 500; i++)
        {
            engine.TryReadPhysics(out _);
            engine.TryReadGraphics(out var graph);
            Assert.InRange(graph.NormalizedCarPosition, 0.0f, 1.0f);
            Assert.InRange(graph.CurrentSectorIndex, 0, 2);
        }
    }
}
