using System.Text.Json.Serialization;
using TelemetryAgent.Core.Models.Memory;

namespace TelemetryAgent.Core.Models;

public sealed class TelemetryPayload
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("packetId")]
    public int PacketId { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("sessionType")]
    public int SessionType { get; set; }

    [JsonPropertyName("carModel")]
    public string CarModel { get; set; } = string.Empty;

    [JsonPropertyName("track")]
    public string Track { get; set; } = string.Empty;

    [JsonPropertyName("speedKmh")]
    public float SpeedKmh { get; set; }

    [JsonPropertyName("gear")]
    public int Gear { get; set; }

    [JsonPropertyName("rpm")]
    public int Rpm { get; set; }

    [JsonPropertyName("steerAngle")]
    public float SteerAngle { get; set; }

    [JsonPropertyName("gas")]
    public float Gas { get; set; }

    [JsonPropertyName("brake")]
    public float Brake { get; set; }

    [JsonPropertyName("clutch")]
    public float Clutch { get; set; }

    [JsonPropertyName("fuel")]
    public float Fuel { get; set; }

    [JsonPropertyName("iCurrentTime")]
    public int ICurrentTime { get; set; }

    [JsonPropertyName("iLastTime")]
    public int ILastTime { get; set; }

    [JsonPropertyName("iBestTime")]
    public int IBestTime { get; set; }

    [JsonPropertyName("completedLaps")]
    public int CompletedLaps { get; set; }

    [JsonPropertyName("normalizedCarPosition")]
    public float NormalizedCarPosition { get; set; }

    [JsonPropertyName("currentSectorIndex")]
    public int CurrentSectorIndex { get; set; }

    [JsonPropertyName("isInPitLane")]
    public int IsInPitLane { get; set; }

    [JsonPropertyName("isInPit")]
    public int IsInPit { get; set; }

    [JsonPropertyName("tc")]
    public float TC { get; set; }

    [JsonPropertyName("abs")]
    public float Abs { get; set; }

    [JsonPropertyName("engineMap")]
    public int EngineMap { get; set; }

    [JsonPropertyName("velocity")]
    public float[] Velocity { get; set; } = [0, 0, 0];

    [JsonPropertyName("accG")]
    public float[] AccG { get; set; } = [0, 0, 0];

    [JsonPropertyName("localAngularVel")]
    public float[] LocalAngularVel { get; set; } = [0, 0, 0];

    [JsonPropertyName("wheelSlip")]
    public float[] WheelSlip { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("wheelsPressure")]
    public float[] WheelsPressure { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("wheelAngularSpeed")]
    public float[] WheelAngularSpeed { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("tyreCoreTemperature")]
    public float[] TyreCoreTemperature { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("brakeTemp")]
    public float[] BrakeTemp { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("suspensionTravel")]
    public float[] SuspensionTravel { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("slipRatio")]
    public float[] SlipRatio { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("slipAngle")]
    public float[] SlipAngle { get; set; } = [0, 0, 0, 0];

    [JsonPropertyName("tyreCompound")]
    public string TyreCompound { get; set; } = string.Empty;
}
