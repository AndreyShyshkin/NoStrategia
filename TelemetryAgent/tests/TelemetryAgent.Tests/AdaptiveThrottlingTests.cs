using TelemetryAgent.Core.Throttling;
using Xunit;

namespace TelemetryAgent.Tests;

public class AdaptiveThrottlingTests
{
    [Fact]
    public void Throttle_FullRateOnTrack_TransmitsAllFrames()
    {
        var throttle = new AdaptiveThrottleController(baseFrequencyHz: 60);

        for (int i = 0; i < 60; i++)
        {
            bool transmit = throttle.ShouldTransmit(status: 2, isInPitLane: 0, out var mode);
            Assert.True(transmit);
            Assert.Equal(ThrottleMode.FullRate, mode);
        }

        var (captured, transmitted, dropped, savings) = throttle.GetStatistics();
        Assert.Equal(60, captured);
        Assert.Equal(60, transmitted);
        Assert.Equal(0, dropped);
        Assert.Equal(0.0, savings);
    }

    [Fact]
    public void Throttle_PitLane_DownsamplesTo10Hz()
    {
        // 60 Hz base / 10 Hz target = factor of 6
        var throttle = new AdaptiveThrottleController(baseFrequencyHz: 60, throttlingEnabled: true);

        int transmittedCount = 0;
        int droppedCount = 0;

        for (int i = 0; i < 60; i++)
        {
            bool transmit = throttle.ShouldTransmit(status: 2, isInPitLane: 1, out var mode);
            if (transmit)
            {
                transmittedCount++;
                Assert.Equal(ThrottleMode.ThrottledPitLane, mode);
            }
            else
            {
                droppedCount++;
                Assert.Equal(ThrottleMode.Suppressed, mode);
            }
        }

        Assert.Equal(10, transmittedCount); // Exactly 10 Hz
        Assert.Equal(50, droppedCount);

        var (_, _, _, savings) = throttle.GetStatistics();
        Assert.InRange(savings, 80.0, 85.0); // ~83.3% bandwidth savings
    }

    [Fact]
    public void Throttle_PauseMenu_ThrottlesToHeartbeat()
    {
        // Standby = 0.5 Hz (every 120 ticks at 60 Hz)
        var throttle = new AdaptiveThrottleController(baseFrequencyHz: 60, throttlingEnabled: true);

        int transmitted = 0;
        for (int i = 0; i < 240; i++)
        {
            if (throttle.ShouldTransmit(status: 3, isInPitLane: 0, out _))
            {
                transmitted++;
            }
        }

        Assert.Equal(2, transmitted); // 2 samples in 4 seconds = 0.5 Hz
        var (_, _, _, savings) = throttle.GetStatistics();
        Assert.True(savings > 95.0, "Pause throttle should achieve >95% savings");
    }

    [Fact]
    public void Throttle_Disabled_PassesAllLiveFrames()
    {
        var throttle = new AdaptiveThrottleController(baseFrequencyHz: 60, throttlingEnabled: false);

        for (int i = 0; i < 60; i++)
        {
            bool transmit = throttle.ShouldTransmit(status: 2, isInPitLane: 1, out var mode);
            Assert.True(transmit);
            Assert.Equal(ThrottleMode.FullRate, mode);
        }
    }

    [Fact]
    public void SteerAngle_ConversionAndDeflection_ComputesCorrectDegrees()
    {
        // 90 degrees in radians = PI / 2 ~= 1.5707963
        float radiansRight = MathF.PI / 2.0f;
        float degreesRight = radiansRight * (180.0f / MathF.PI);
        Assert.Equal(90.0f, MathF.Round(degreesRight, 1));

        float radiansLeft = -MathF.PI / 4.0f;
        float degreesLeft = radiansLeft * (180.0f / MathF.PI);
        Assert.Equal(-45.0f, MathF.Round(degreesLeft, 1));
    }
}
