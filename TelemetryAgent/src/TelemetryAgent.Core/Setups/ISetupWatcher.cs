using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Setups;

public readonly record struct LiveCarSetupContext(
    float Fuel = -1f,
    int Tc = -1,
    int Abs = -1,
    int EngineMap = -1,
    float[]? CamberRad = null,
    bool InGarage = false);

public interface ISetupWatcher : IDisposable
{
    bool IsActive { get; }
    string WatchDirectory { get; }
    string LastSyncedFile { get; }
    string LastSyncedHash { get; }
    DateTime? LastSyncedTime { get; }

    event Action<SetupPayload>? SetupDetected;

    void Start();
    void Stop();
    void SetDirectory(string directoryPath);
    Task<SetupPayload?> ProcessSetupFileAsync(string filePath, bool force = false, CancellationToken ct = default);
    Task<SetupPayload?> ScanExistingSetupsAsync(bool force = false, CancellationToken ct = default);
    Task<SetupPayload?> TryLoadSetupForCarAndTrackAsync(
        string carModel,
        string track,
        LiveCarSetupContext? liveContext = null,
        bool force = false,
        CancellationToken ct = default);
}
