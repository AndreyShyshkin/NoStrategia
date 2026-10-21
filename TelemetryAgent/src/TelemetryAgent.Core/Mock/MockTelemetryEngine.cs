using TelemetryAgent.Core.Models.Memory;
using TelemetryAgent.Core.SharedMemory;

namespace TelemetryAgent.Core.Mock;

/// <summary>
/// Autonomous GT3 physics and session telemetry simulator.
/// Simulates a Mercedes-AMG GT3 Evo lapping Circuit de Spa-Francorchamps with high fidelity,
/// supporting 60/100 Hz deterministic tick loops and synthetic anomaly injections.
/// </summary>
public sealed class MockTelemetryEngine : ITelemetryReader
{
    private readonly SyntheticAnomalyInjector _anomalyInjector = new();
    private readonly object _lock = new();

    private int _packetId;
    private double _trackDistanceMeters;
    private const double TrackLengthMeters = 7004.0; // Spa-Francorchamps
    private int _completedLaps;
    private int _lapTimeMs;
    private int _lastLapTimeMs = 137450; // 2:17.450
    private int _bestLapTimeMs = 136890; // 2:16.890
    private float _fuel = 48.5f;

    private bool _isConnected = true;

    public SyntheticAnomalyInjector AnomalyInjector => _anomalyInjector;
    public bool IsConnected => _isConnected;
    public string SourceName => "Autonomous Mock GT3 Engine (Spa-Francorchamps)";

    public bool Connect()
    {
        _isConnected = true;
        return true;
    }

    public void Disconnect()
    {
        _isConnected = false;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _packetId = 0;
            _trackDistanceMeters = 0;
            _completedLaps = 0;
            _lapTimeMs = 0;
            _fuel = 48.5f;
            _anomalyInjector.Clear();
        }
    }

    public bool TryReadPhysics(out SPageFilePhysics physics)
    {
        physics = default;
        if (!_isConnected) return false;

        lock (_lock)
        {
            _packetId++;
            double s = (_trackDistanceMeters % TrackLengthMeters) / TrackLengthMeters;

            // Generate kinematic profile along Spa track
            // La Source (s: 0.0 - 0.08): 65-120 km/h
            // Eau Rouge / Raidillon (s: 0.08 - 0.18): 180-230 km/h
            // Kemmel Straight (s: 0.18 - 0.32): 230-275 km/h
            // Les Combes (s: 0.32 - 0.40): 110-140 km/h
            // Bruxelles / Rivage (s: 0.40 - 0.52): 95-130 km/h
            // Pouhon (s: 0.52 - 0.65): 190-215 km/h
            // Fagnes / Campus (s: 0.65 - 0.75): 140-170 km/h
            // Stavelot / Blanchimont (s: 0.75 - 0.92): 220-280 km/h
            // Bus Stop Chicane (s: 0.92 - 1.00): 65-100 km/h

            float targetSpeed;
            float gas;
            float brake;
            float steer;
            int gear;
            float lateralG;

            if (s is >= 0.18 and < 0.32 or >= 0.78 and < 0.92)
            {
                // High-speed straights (Kemmel & Blanchimont)
                targetSpeed = 265f + 15f * MathF.Sin((float)s * 40f);
                gas = 1.0f;
                brake = 0.0f;
                steer = 0.02f * MathF.Sin((float)s * 50f);
                gear = 6;
                lateralG = 0.2f;
            }
            else if (s is >= 0.30 and < 0.35 or >= 0.92 and < 0.96)
            {
                // Heavy braking zones (Les Combes & Bus Stop)
                targetSpeed = 85f;
                gas = 0.0f;
                brake = 0.85f;
                steer = 0.05f;
                gear = 2;
                lateralG = -0.3f;
            }
            else if (s is >= 0.52 and < 0.62)
            {
                // Pouhon high-speed double-apex left
                targetSpeed = 205f;
                gas = 0.65f;
                brake = 0.0f;
                steer = -0.22f; // Left turn
                gear = 4;
                lateralG = -2.6f; // High cornering load
            }
            else
            {
                // Mid-speed twisty sectors
                targetSpeed = 145f + 30f * MathF.Sin((float)s * 25f);
                gas = 0.75f;
                brake = 0.0f;
                steer = 0.18f * MathF.Sin((float)s * 30f);
                gear = 3;
                lateralG = 1.4f * MathF.Sin((float)s * 20f);
            }

            int rpm = Math.Clamp((int)(targetSpeed * 27.5f) + 2000, 4200, 7400);

            // Advance vehicle position (16.66 ms tick delta at 60Hz)
            double metersPerSecond = targetSpeed / 3.6;
            double distanceIncrement = metersPerSecond * 0.016666;
            _trackDistanceMeters += distanceIncrement;
            _lapTimeMs += 17;

            // Fuel consumption (~3.3L per lap at Spa)
            _fuel = MathF.Max(0.5f, _fuel - (float)(distanceIncrement / TrackLengthMeters * 3.3 * 0.01));

            // Wheel angular velocities (omega = v / radius), AMG GT3 tyre radius ~0.335m
            float baseOmega = (float)(metersPerSecond / 0.335);
            var wheelSpeed = new[] { baseOmega, baseOmega, baseOmega * 1.01f, baseOmega * 1.01f };
            var wheelSlip = new[] { 0.02f, 0.02f, 0.04f, 0.04f };
            var slipRatio = new[] { -0.01f, -0.01f, 0.03f, 0.03f };
            var slipAngle = new[] { steer * 0.4f, steer * 0.4f, 0.02f, 0.02f };
            var localAngVel = new[] { lateralG * 0.15f, 0.0f, 0.0f };

            int isInPitLane = 0;

            // Apply synthetic anomalies if active
            _anomalyInjector.ApplyAnomaly(
                ref brake,
                ref steer,
                ref _fuel,
                ref isInPitLane,
                wheelSpeed,
                wheelSlip,
                slipRatio,
                slipAngle,
                localAngVel);

            physics = new SPageFilePhysics
            {
                PacketId = _packetId,
                Gas = gas,
                Brake = brake,
                Fuel = _fuel,
                Gear = gear,
                Rpm = rpm,
                SteerAngle = steer,
                SpeedKmh = targetSpeed,
                Velocity = [(float)metersPerSecond, 0, 0],
                AccG = [lateralG, brake > 0 ? -1.8f : 0.8f, 1.0f],
                LocalAngularVel = localAngVel,
                WheelSlip = wheelSlip,
                WheelAngularSpeed = wheelSpeed,
                SlipRatio = slipRatio,
                SlipAngle = slipAngle,
                WheelsPressure = [27.6f, 27.8f, 27.4f, 27.5f],
                TyreCoreTemperature = [88.5f, 89.2f, 86.4f, 87.1f],
                BrakeTemp = [480f, 495f, 380f, 390f],
                SuspensionTravel = [0.035f, 0.034f, 0.042f, 0.041f],
                TC = 3.0f,
                Abs = 3.0f,
                EngineMap = 1
            };

            return true;
        }
    }

    public bool TryReadGraphics(out SPageFileGraphic graphics)
    {
        graphics = default;
        if (!_isConnected) return false;

        lock (_lock)
        {
            double s = (_trackDistanceMeters % TrackLengthMeters) / TrackLengthMeters;

            // Lap completion detection
            if (_trackDistanceMeters >= TrackLengthMeters * (_completedLaps + 1))
            {
                _completedLaps++;
                _lastLapTimeMs = _lapTimeMs;
                if (_lapTimeMs < _bestLapTimeMs) _bestLapTimeMs = _lapTimeMs;
                _lapTimeMs = 0;
            }

            // Sectors for Spa: S1 ends ~0.31, S2 ends ~0.74, S3 ends 1.0
            int sector = s < 0.31 ? 0 : s < 0.74 ? 1 : 2;

            int isInPitLane = 0;
            float dummyBrake = 0;
            float dummySteer = 0;
            float dummyFuel = _fuel;
            _anomalyInjector.ApplyAnomaly(
                ref dummyBrake,
                ref dummySteer,
                ref dummyFuel,
                ref isInPitLane,
                [], [], [], [], []);

            graphics = new SPageFileGraphic
            {
                PacketId = _packetId,
                Status = 2, // AC_LIVE
                Session = 2, // Race
                CurrentTime = FormatTime(_lapTimeMs),
                LastTime = FormatTime(_lastLapTimeMs),
                BestTime = FormatTime(_bestLapTimeMs),
                ICurrentTime = _lapTimeMs,
                ILastTime = _lastLapTimeMs,
                IBestTime = _bestLapTimeMs,
                CompletedLaps = _completedLaps,
                Position = 3,
                NormalizedCarPosition = (float)s,
                CurrentSectorIndex = sector,
                IsInPitLane = isInPitLane,
                IsInPit = 0,
                TyreCompound = "DHE"
            };

            return true;
        }
    }

    public bool TryReadStatic(out SPageFileStatic staticData)
    {
        staticData = new SPageFileStatic
        {
            CarModel = "amg_gt3_evo",
            Track = "spa",
            PlayerName = "Andrey",
            PlayerSurname = "Shyshkin",
            PlayerNick = "NoStrategia_Pilot",
            MaxRpm = 7500,
            MaxFuel = 120.0f,
            SectorCount = 3,
            TrackSplineLength = (float)TrackLengthMeters
        };
        return true;
    }

    private static string FormatTime(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    public void Dispose()
    {
        Disconnect();
    }
}
