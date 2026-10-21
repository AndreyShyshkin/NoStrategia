using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Network;

/// <summary>
/// High-throughput HTTP client for transmitting telemetry frames and setup updates
/// to the Ingestion Gateway API with latency tracking and backpressure resilience.
/// </summary>
public sealed class IngestionHttpClient : IIngestionClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Stopwatch _pingStopwatch = new();

    public string TelemetryEndpoint { get; set; } = "http://127.0.0.1:4000/api/v1/telemetry";
    public string SetupEndpoint { get; set; } = "http://127.0.0.1:4000/api/v1/setups/sync";

    public bool IsConnected { get; private set; }
    public double LastLatencyMs { get; private set; }
    public long SuccessCount { get; private set; }
    public long FailureCount { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public IngestionHttpClient(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 10,
                EnableMultipleHttp2Connections = true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(500) // Fast fail to avoid pipeline stalls
            };
            _ownsClient = true;
        }
    }

    public async Task<bool> SendTelemetryAsync(TelemetryPayload payload, CancellationToken ct = default)
    {
        try
        {
            _pingStopwatch.Restart();
            using var response = await _httpClient.PostAsJsonAsync(TelemetryEndpoint, payload, ct);
            _pingStopwatch.Stop();
            LastLatencyMs = _pingStopwatch.Elapsed.TotalMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                SuccessCount++;
                IsConnected = true;
                LastError = string.Empty;
                return true;
            }
            else
            {
                FailureCount++;
                LastError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                IsConnected = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            FailureCount++;
            LastError = ex.Message;
            IsConnected = false;
            return false;
        }
    }

    public async Task<bool> SendSetupAsync(SetupPayload payload, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(SetupEndpoint, payload, ct);
            if (response.IsSuccessStatusCode)
            {
                SuccessCount++;
                return true;
            }
            else
            {
                FailureCount++;
                LastError = $"Setup sync failed: {(int)response.StatusCode}";
                return false;
            }
        }
        catch (Exception ex)
        {
            FailureCount++;
            LastError = $"Setup sync error: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> PingServerAsync(CancellationToken ct = default)
    {
        try
        {
            var baseUri = new Uri(TelemetryEndpoint);
            string healthUrl = $"{baseUri.Scheme}://{baseUri.Authority}/health";

            _pingStopwatch.Restart();
            using var response = await _httpClient.GetAsync(healthUrl, ct);
            _pingStopwatch.Stop();
            LastLatencyMs = _pingStopwatch.Elapsed.TotalMilliseconds;

            IsConnected = response.IsSuccessStatusCode;
            return IsConnected;
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
