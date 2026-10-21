namespace TelemetryAgent.Core.Services;

public enum DataSourceType
{
    LiveMemory,   // Windows/Wine ACC Shared Memory
    MockEngine,   // Autonomous GT3 Spa Simulator
    TraceReplay   // NDJSON Benchmark Log Replayer
}

public sealed class AgentConfiguration
{
    public string IngestionUrl { get; set; } = "http://127.0.0.1:4000/api/v1/telemetry";
    public string SetupSyncUrl { get; set; } = "http://127.0.0.1:4000/api/v1/setups/sync";
    public int TargetFrequencyHz { get; set; } = 60; // 60 or 100
    public bool EnableAdaptiveThrottling { get; set; } = true;
    public DataSourceType DataSource { get; set; } = DataSourceType.LiveMemory;
    public string SetupsDirectory { get; set; } = string.Empty;
    public string TraceReplayPath { get; set; } = string.Empty;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool AutoStartCapture { get; set; } = true;

    public AgentConfiguration Clone()
    {
        return (AgentConfiguration)MemberwiseClone();
    }
}
