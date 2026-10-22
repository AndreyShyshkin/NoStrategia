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

            // High-fidelity kinematic model along Spa-Francorchamps (7004m)
            float targetSpeed;
            float gas;
            float brake;
            float steer;
            int gear;
            float lateralG;

            if (s is >= 0.00 and < 0.06)
            {
                // Turn 1: La Source Hairpin (slow, tight right)
                // Severe braking from straight, 1st gear, high steering lock (up to ~50 deg)
                if (s < 0.02)
                {
                    targetSpeed = 68f;
                    gas = 0.05f;
                    brake = 0.70f;
                    steer = 0.85f; // ~48.7° right
                    gear = 2; // ACC 1st gear
                    lateralG = 1.6f;
                }
                else
                {
                    targetSpeed = 105f;
                    gas = 0.85f;
                    brake = 0.0f;
                    steer = 0.35f;
                    gear = 3; // ACC 2nd gear
                    lateralG = 1.2f;
                }
            }
            else if (s is >= 0.06 and < 0.16)
            {
                // Eau Rouge & Raidillon uphill sweep (left-right-left uphill crest)
                targetSpeed = 220f + 15f * MathF.Sin((float)s * 35f);
                gas = 1.0f;
                brake = 0.0f;
                steer = -0.16f * MathF.Sin((float)(s - 0.06) * 50f);
                gear = targetSpeed > 210f ? 6 : 5; // ACC 5th/4th gear
                lateralG = 2.1f * MathF.Cos((float)(s - 0.06) * 50f);
            }
            else if (s is >= 0.16 and < 0.28)
            {
                // Kemmel Straight (Top speed draft run)
                targetSpeed = 265f + 12f * MathF.Sin((float)s * 30f);
                gas = 1.0f;
                brake = 0.0f;
                steer = 0.01f * MathF.Sin((float)s * 60f);
                gear = 7; // ACC 6th gear
                lateralG = 0.1f;
            }
            else if (s is >= 0.28 and < 0.36)
            {
                // Les Combes & Malmedy Chicane
                if (s < 0.31)
                {
                    // Heavy threshold braking
                    targetSpeed = 105f;
                    gas = 0.0f;
                    brake = 0.90f;
                    steer = 0.05f;
                    gear = 4; // ACC 3rd gear
                    lateralG = -0.4f;
                }
                else
                {
                    // Right-left chicane
                    targetSpeed = 125f;
                    gas = 0.70f;
                    brake = 0.0f;
                    steer = 0.40f * MathF.Sin((float)(s - 0.31) * 80f);
                    gear = 4; // ACC 3rd gear
                    lateralG = 1.8f * MathF.Sin((float)(s - 0.31) * 80f);
                }
            }
            else if (s is >= 0.36 and < 0.48)
            {
                // Bruxelles / Rivage Downhill Hairpin (long continuous right)
                targetSpeed = 92f + 18f * (float)(s - 0.36) / 0.12f;
                gas = 0.65f;
                brake = 0.0f;
                steer = 0.68f; // ~39° right
                gear = 3; // ACC 2nd gear
                lateralG = 1.85f;
            }
            else if (s is >= 0.48 and < 0.54)
            {
                // Corner 9 (Speaker's Corner / Liege)
                targetSpeed = 150f;
                gas = 0.85f;
                brake = 0.0f;
                steer = -0.32f; // ~18.3° left
                gear = 4; // ACC 3rd gear
                lateralG = -1.9f;
            }
            else if (s is >= 0.54 and < 0.65)
            {
                // Pouhon Double-Apex Left (Fastest, highest G corner)
                targetSpeed = 202f + 8f * MathF.Sin((float)s * 25f);
                gas = 0.72f;
                brake = 0.0f;
                steer = -0.42f; // ~24° left
                gear = 5; // ACC 4th gear
                lateralG = -2.65f; // High cornering load
            }
            else if (s is >= 0.65 and < 0.74)
            {
                // Les Fagnes Chicane (Pif-Paf)
                targetSpeed = 140f + 15f * MathF.Sin((float)s * 30f);
                gas = 0.75f;
                brake = 0.0f;
                steer = 0.36f * MathF.Sin((float)(s - 0.65) * 65f);
                gear = 4; // ACC 3rd gear
                lateralG = 1.7f * MathF.Sin((float)(s - 0.65) * 65f);
            }
            else if (s is >= 0.74 and < 0.82)
            {
                // Campus & Stavelot acceleration
                targetSpeed = 160f + 70f * (float)(s - 0.74) / 0.08f;
                gas = 1.0f;
                brake = 0.0f;
                steer = 0.22f * (1.0f - (float)(s - 0.74) / 0.08f);
                gear = targetSpeed > 210f ? 6 : targetSpeed > 185f ? 5 : 4;
                lateralG = 1.3f;
            }
            else if (s is >= 0.82 and < 0.92)
            {
                // Blanchimont Flat-Out Sweeper
                targetSpeed = 270f + 10f * MathF.Sin((float)s * 30f);
                gas = 1.0f;
                brake = 0.0f;
                steer = -0.06f; // Gentle left kink
                gear = 7; // ACC 6th gear
                lateralG = -1.2f;
            }
            else
            {
                // Bus Stop Chicane (s: 0.92 to 1.00)
                if (s < 0.95)
                {
                    // Brutal braking from 280 km/h to 60 km/h
                    targetSpeed = 62f;
                    gas = 0.0f;
                    brake = 0.95f;
                    steer = 0.05f;
                    gear = 2; // ACC 1st gear
                    lateralG = -0.4f;
                }
                else if (s < 0.98)
                {
                    // Sharp right-left chicane (tests max per-car steering lock)
                    targetSpeed = 75f;
                    gas = 0.40f;
                    brake = 0.0f;
                    steer = 0.90f * MathF.Sin((float)(s - 0.95) * 105f); // up to ~51.5° steering lock!
                    gear = 2; // ACC 1st gear
                    lateralG = 1.6f * MathF.Sin((float)(s - 0.95) * 105f);
                }
                else
                {
                    // Exit onto start-finish straight
                    targetSpeed = 120f + 35f * (float)(s - 0.98) / 0.02f;
                    gas = 1.0f;
                    brake = 0.0f;
                    steer = 0.02f;
                    gear = 3; // ACC 2nd gear
                    lateralG = 0.2f;
                }
            }

            // Compute realistic GT3 engine RPM based on current gear and speed
            int rpm = gear switch
            {
                2 => Math.Clamp((int)(4600 + (targetSpeed - 55f) * 65f), 4500, 7450),  // 1st gear (ACC 2)
                3 => Math.Clamp((int)(4800 + (targetSpeed - 85f) * 55f), 4800, 7450),  // 2nd gear (ACC 3)
                4 => Math.Clamp((int)(5100 + (targetSpeed - 120f) * 45f), 5100, 7450), // 3rd gear (ACC 4)
                5 => Math.Clamp((int)(5300 + (targetSpeed - 165f) * 40f), 5300, 7450), // 4th gear (ACC 5)
                6 => Math.Clamp((int)(5500 + (targetSpeed - 210f) * 38f), 5500, 7450), // 5th gear (ACC 6)
                7 => Math.Clamp((int)(5800 + (targetSpeed - 250f) * 45f), 5800, 7400), // 6th gear (ACC 7)
                _ => 6000
            };

            // Dynamic thermal physics
            float frontBrakeTemp = brake > 0.4f ? 520f + brake * 110f : 390f + MathF.Sin((float)s * 10f) * 25f;
            float rearBrakeTemp = brake > 0.4f ? 430f + brake * 80f : 350f + MathF.Sin((float)s * 10f) * 20f;

            // Outside tyres in corners absorb more thermal energy
            float flTemp = 87.5f + (lateralG > 1.2f ? 4.5f : 0f) + (brake > 0.5f ? 3.0f : 0f);
            float frTemp = 88.0f + (lateralG < -1.2f ? 5.2f : 0f) + (brake > 0.5f ? 3.0f : 0f);
            float rlTemp = 86.2f + (gas > 0.9f ? 2.5f : 0f);
            float rrTemp = 86.8f + (gas > 0.9f ? 2.5f : 0f);

            // Pressures expand slightly with heat
            float flPsi = 27.4f + (flTemp - 85f) * 0.04f;
            float frPsi = 27.5f + (frTemp - 85f) * 0.04f;
            float rlPsi = 27.3f + (rlTemp - 85f) * 0.03f;
            float rrPsi = 27.4f + (rrTemp - 85f) * 0.03f;

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
                WheelsPressure = [flPsi, frPsi, rlPsi, rrPsi],
                TyreCoreTemperature = [flTemp, frTemp, rlTemp, rrTemp],
                BrakeTemp = [frontBrakeTemp, frontBrakeTemp, rearBrakeTemp, rearBrakeTemp],
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
