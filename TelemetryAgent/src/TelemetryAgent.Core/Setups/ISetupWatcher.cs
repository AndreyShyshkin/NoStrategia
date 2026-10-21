using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Setups;

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
    Task<SetupPayload?> TryLoadSetupForCarAndTrackAsync(string carModel, string track, CancellationToken ct = default);
}
