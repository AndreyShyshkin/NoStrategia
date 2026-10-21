using TelemetryAgent.Core.Mock;
using Xunit;

namespace TelemetryAgent.Tests;

public class AnomalyInjectionTests
{
    [Fact]
    public void AnomalyInjector_WheelLockup_SetsFrontLeftWheelSpeedToZero()
    {
        var engine = new MockTelemetryEngine();
        engine.AnomalyInjector.InjectAnomaly(AnomalyType.WheelLockup, durationTicks: 10);

        Assert.True(engine.AnomalyInjector.HasActiveAnomaly);
        Assert.Equal(AnomalyType.WheelLockup, engine.AnomalyInjector.ActiveAnomaly);

        engine.TryReadPhysics(out var phys);

        Assert.Equal(1.0f, phys.Brake);
        Assert.Equal(0.0f, phys.WheelAngularSpeed[0]); // FL wheel locked
        Assert.True(phys.SlipRatio[0] < -0.5f, "Slip ratio should spike negative under lockup");
    }

    [Fact]
    public void AnomalyInjector_OversteerSpin_IncreasesYawRateAndSlipAngle()
    {
        var engine = new MockTelemetryEngine();
        engine.AnomalyInjector.InjectAnomaly(AnomalyType.OversteerSpin, durationTicks: 15);

        engine.TryReadPhysics(out var phys);

        Assert.True(phys.LocalAngularVel[0] > 0.3f, "Yaw rate should be elevated during spin");
        Assert.True(phys.SlipAngle[2] > 0.2f, "Rear tyre slip angle should be elevated");
    }

    [Fact]
    public void AnomalyInjector_FuelShortage_ReducesFuelLevel()
    {
        var engine = new MockTelemetryEngine();
        engine.AnomalyInjector.InjectAnomaly(AnomalyType.FuelShortage, durationTicks: 5);

        engine.TryReadPhysics(out var phys);

        Assert.True(phys.Fuel < 2.0f, "Fuel should drop to critical level");
    }

    [Fact]
    public void AnomalyInjector_ExpiresAfterSpecifiedDuration()
    {
        var engine = new MockTelemetryEngine();
        engine.AnomalyInjector.InjectAnomaly(AnomalyType.WheelLockup, durationTicks: 3);

        for (int i = 0; i < 3; i++)
        {
            engine.TryReadPhysics(out _);
        }

        // 4th tick should be cleared
        engine.TryReadPhysics(out var physAfter);
        Assert.False(engine.AnomalyInjector.HasActiveAnomaly);
        Assert.True(physAfter.WheelAngularSpeed[0] > 0);
    }
}
