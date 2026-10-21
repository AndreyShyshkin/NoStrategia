using TelemetryAgent.Core.Models.Memory;

namespace TelemetryAgent.Core.Models;

public static class TelemetryMapper
{
    public static TelemetryPayload MapToPayload(
        in SPageFilePhysics physics,
        in SPageFileGraphic graphics,
        in SPageFileStatic staticData)
    {
        return new TelemetryPayload
        {
            Timestamp = DateTime.UtcNow,
            PacketId = physics.PacketId,
            Status = graphics.Status,
            SessionType = graphics.Session,
            CarModel = staticData.CarModel ?? string.Empty,
            Track = staticData.Track ?? string.Empty,
            SpeedKmh = physics.SpeedKmh,
            Gear = physics.Gear,
            Rpm = physics.Rpm,
            SteerAngle = physics.SteerAngle,
            Gas = physics.Gas,
            Brake = physics.Brake,
            Clutch = physics.Clutch,
            Fuel = physics.Fuel,
            ICurrentTime = graphics.ICurrentTime,
            ILastTime = graphics.ILastTime,
            IBestTime = graphics.IBestTime,
            CompletedLaps = graphics.CompletedLaps,
            NormalizedCarPosition = graphics.NormalizedCarPosition,
            CurrentSectorIndex = graphics.CurrentSectorIndex,
            IsInPitLane = graphics.IsInPitLane,
            IsInPit = graphics.IsInPit,
            TC = physics.TC,
            Abs = physics.Abs,
            EngineMap = physics.EngineMap,
            Velocity = CopyArray(physics.Velocity, 3),
            AccG = CopyArray(physics.AccG, 3),
            LocalAngularVel = CopyArray(physics.LocalAngularVel, 3),
            WheelSlip = CopyArray(physics.WheelSlip, 4),
            WheelsPressure = CopyArray(physics.WheelsPressure, 4),
            WheelAngularSpeed = CopyArray(physics.WheelAngularSpeed, 4),
            TyreCoreTemperature = CopyArray(physics.TyreCoreTemperature, 4),
            BrakeTemp = CopyArray(physics.BrakeTemp, 4),
            SuspensionTravel = CopyArray(physics.SuspensionTravel, 4),
            SlipRatio = CopyArray(physics.SlipRatio, 4),
            SlipAngle = CopyArray(physics.SlipAngle, 4),
            TyreCompound = graphics.TyreCompound ?? string.Empty
        };
    }

    private static float[] CopyArray(float[]? source, int expectedLength)
    {
        if (source == null || source.Length == 0)
            return new float[expectedLength];

        var dest = new float[expectedLength];
        Array.Copy(source, dest, Math.Min(source.Length, expectedLength));
        return dest;
    }
}
