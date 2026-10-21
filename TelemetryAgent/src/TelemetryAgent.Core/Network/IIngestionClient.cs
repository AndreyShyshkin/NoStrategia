using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Network;

public interface IIngestionClient : IDisposable
{
    string TelemetryEndpoint { get; set; }
    string SetupEndpoint { get; set; }
    bool IsConnected { get; }
    double LastLatencyMs { get; }
    long SuccessCount { get; }
    long FailureCount { get; }
    string LastError { get; }

    Task<bool> SendTelemetryAsync(TelemetryPayload payload, CancellationToken ct = default);
    Task<bool> SendSetupAsync(SetupPayload payload, CancellationToken ct = default);
    Task<bool> PingServerAsync(CancellationToken ct = default);
}
