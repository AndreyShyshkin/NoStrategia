namespace TelemetryAgent.Core.Mock;

public enum AnomalyType
{
    None,
    WheelLockup,    // Front-left wheel speed drops to 0 rad/s under 100% braking
    OversteerSpin,  // Yaw rate spikes +0.45 rad/s above bicycle model expectation
    FuelShortage,   // Fuel drops below critical threshold (e.g., 1.5 L)
    PitLaneEntry    // Forces IsInPitLane = 1 to test adaptive throttling transition
}

public sealed class SyntheticAnomalyInjector
{
    private AnomalyType _activeAnomaly = AnomalyType.None;
    private int _remainingTicks;
    private readonly object _lock = new();

    public AnomalyType ActiveAnomaly { get { lock (_lock) return _activeAnomaly; } }
    public bool HasActiveAnomaly { get { lock (_lock) return _activeAnomaly != AnomalyType.None; } }

    public void InjectAnomaly(AnomalyType type, int durationTicks = 90) // Default 1.5 sec @ 60 Hz
    {
        lock (_lock)
        {
            _activeAnomaly = type;
            _remainingTicks = Math.Max(1, durationTicks);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _activeAnomaly = AnomalyType.None;
            _remainingTicks = 0;
        }
    }

    public void ApplyAnomaly(
        ref float brake,
        ref float steerAngle,
        ref float fuel,
        ref int isInPitLane,
        float[] wheelAngularSpeed,
        float[] wheelSlip,
        float[] slipRatio,
        float[] slipAngle,
        float[] localAngularVel)
    {
        lock (_lock)
        {
            if (_activeAnomaly == AnomalyType.None || _remainingTicks <= 0)
            {
                _activeAnomaly = AnomalyType.None;
                return;
            }

            switch (_activeAnomaly)
            {
                case AnomalyType.WheelLockup:
                    // Trigger P1 Lockup: Brake = 1.0, FL wheel locks (0 rad/s), slip ratio spikes negative
                    brake = 1.0f;
                    if (wheelAngularSpeed.Length > 0) wheelAngularSpeed[0] = 0.0f; // FL wheel stops
                    if (slipRatio.Length > 0) slipRatio[0] = -0.88f;
                    if (wheelSlip.Length > 0) wheelSlip[0] = 0.95f;
                    break;

                case AnomalyType.OversteerSpin:
                    // Trigger Spin Detector: Yaw rate deviation +0.45 rad/s from kinematic model
                    if (localAngularVel.Length > 0) localAngularVel[0] += 0.48f; // Yaw rate spike
                    if (slipAngle.Length > 2)
                    {
                        slipAngle[2] = 14.5f * (MathF.PI / 180f); // RL tire heavy slide
                        slipAngle[3] = 13.8f * (MathF.PI / 180f); // RR tire heavy slide
                    }
                    steerAngle = -0.35f; // Counter-steering reaction
                    break;

                case AnomalyType.FuelShortage:
                    // Trigger P2 Strategy Alert: Drop fuel to 1.1 Liters
                    fuel = 1.15f;
                    break;

                case AnomalyType.PitLaneEntry:
                    isInPitLane = 1;
                    break;
            }

            _remainingTicks--;
            if (_remainingTicks <= 0)
            {
                _activeAnomaly = AnomalyType.None;
            }
        }
    }
}
