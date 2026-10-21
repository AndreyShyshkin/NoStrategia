using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelemetryAgent.Core.Models;

public sealed class SetupPayload
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("carModel")]
    public string CarModel { get; set; } = string.Empty;

    [JsonPropertyName("track")]
    public string Track { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("setupData")]
    public JsonElement SetupData { get; set; }
}
