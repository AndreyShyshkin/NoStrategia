namespace TelemetryAgent.Core.Models;

public enum AgentState
{
    Stopped,
    Starting,
    RunningLive,       // Full frequency (60 / 100 Hz)
    RunningThrottled,  // Pit lane downsample (10 Hz)
    StandbyHeartbeat,  // Pause / Menu (0.5 Hz)
    ReplayingMock      // Autonomous mock simulation
}

public enum GameLinkState
{
    Searching,         // Polling for ACC Shared Memory
    Connected,         // MMF mapped successfully
    Disconnected,      // Game closed or unmapped
    MockEmulation      // Running autonomous test generator
}

public enum BackendLinkState
{
    Disconnected,
    Connecting,
    Connected,
    BackpressureWarning
}

public sealed class AgentStatus
{
    public AgentState State { get; set; } = AgentState.Stopped;
    public GameLinkState GameLink { get; set; } = GameLinkState.Searching;
    public BackendLinkState BackendLink { get; set; } = BackendLinkState.Disconnected;
    public bool SetupWatcherActive { get; set; }

    public double CurrentHz { get; set; }
    public long TotalPacketsCaptured { get; set; }
    public long TotalPacketsSent { get; set; }
    public long TotalPacketsDropped { get; set; }
    public double BandwidthSavingsPercentage { get; set; }

    public string CurrentCarModel { get; set; } = string.Empty;
    public string CurrentTrack { get; set; } = string.Empty;
    public string IngestionEndpoint { get; set; } = string.Empty;
    public string LastSyncedSetupName { get; set; } = string.Empty;
    public DateTime? LastSyncedSetupTime { get; set; }

    public double LastServerLatencyMs { get; set; }
    public string LastError { get; set; } = string.Empty;
}
