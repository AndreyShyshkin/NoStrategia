using System.Diagnostics;
using TelemetryAgent.Core.Buffer;
using TelemetryAgent.Core.Mock;
using TelemetryAgent.Core.Models;
using TelemetryAgent.Core.Models.Memory;
using TelemetryAgent.Core.Network;
using TelemetryAgent.Core.Setups;
using TelemetryAgent.Core.SharedMemory;
using TelemetryAgent.Core.Throttling;

namespace TelemetryAgent.Core.Services;

/// <summary>
/// Core orchestrator for the Edge Telemetry Agent. Coordinates high-frequency unmanaged memory reads,
/// state-driven adaptive throttling, ring buffer staging, HTTP ingestion dispatch,
/// and JSON setup change interception.
/// </summary>
public sealed class TelemetryAgentCoordinator : IAsyncDisposable
{
    private readonly AgentConfiguration _config;
    private ITelemetryReader _reader;
    private readonly IThrottlePolicy _throttlePolicy;
    private readonly LockFreeRingBuffer<TelemetryPayload> _ringBuffer;
    private readonly IIngestionClient _ingestionClient;
    private readonly ISetupWatcher _setupWatcher;

    private readonly MockTelemetryEngine _mockEngine = new();
    private readonly TraceReplayer _traceReplayer = new();
    private readonly WindowsSharedMemoryReader _liveReader = new();

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private Task? _dispatchTask;
    private Task? _healthCheckTask;

    private readonly AgentStatus _status = new();
    private readonly object _statusLock = new();

    private long _capturedTotal;
    private long _sentTotal;
    private long _droppedTotal;

    private readonly Stopwatch _hzStopwatch = new();
    private int _hzCounter;
    private double _effectiveHz;

    private string _lastQueriedCar = string.Empty;

    public AgentStatus CurrentStatus { get { lock (_statusLock) return _status; } }
    public AgentConfiguration Configuration => _config;
    public SyntheticAnomalyInjector AnomalyInjector => _mockEngine.AnomalyInjector;
    public ISetupWatcher SetupWatcher => _setupWatcher;

    public event Action<AgentStatus>? StatusChanged;
    public event Action<TelemetryPayload>? FrameDispatched;
    public event Action<SetupPayload>? SetupSynced;

    public Task<SetupPayload?> RefreshSetupsAsync() => _setupWatcher.ScanExistingSetupsAsync();

    public TelemetryAgentCoordinator(
        AgentConfiguration? configuration = null,
        IIngestionClient? ingestionClient = null,
        ISetupWatcher? setupWatcher = null)
    {
        _config = configuration ?? new AgentConfiguration();
        _throttlePolicy = new AdaptiveThrottleController(_config.TargetFrequencyHz, _config.EnableAdaptiveThrottling);
        _ringBuffer = new LockFreeRingBuffer<TelemetryPayload>(2048);
        _ingestionClient = ingestionClient ?? new IngestionHttpClient();
        _setupWatcher = setupWatcher ?? new SetupWatcherService(
            string.IsNullOrEmpty(_config.SetupsDirectory) ? null : _config.SetupsDirectory);

        _reader = SelectReader(_config.DataSource);
        _setupWatcher.SetupDetected += OnSetupDetected;
    }

    private ITelemetryReader SelectReader(DataSourceType dataSource) => dataSource switch
    {
        DataSourceType.MockEngine => _mockEngine,
        DataSourceType.TraceReplay => _traceReplayer,
        _ => _liveReader
    };

    public void SwitchDataSource(DataSourceType dataSource)
    {
        _config.DataSource = dataSource;
        _reader = SelectReader(dataSource);
        _reader.Connect();
    }

    public async Task StartAsync()
    {
        if (_cts != null && !_cts.IsCancellationRequested) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _reader.Connect();
        _setupWatcher.Start();

        _hzStopwatch.Restart();
        _hzCounter = 0;

        _captureTask = Task.Run(() => RunCaptureLoopAsync(ct), ct);
        _dispatchTask = Task.Run(() => RunDispatchLoopAsync(ct), ct);
        _healthCheckTask = Task.Run(() => RunHealthCheckLoopAsync(ct), ct);

        UpdateStatus(s =>
        {
            s.State = AgentState.Starting;
            s.SetupWatcherActive = _setupWatcher.IsActive;
        });
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try
        {
            if (_captureTask != null) await _captureTask;
            if (_dispatchTask != null) await _dispatchTask;
            if (_healthCheckTask != null) await _healthCheckTask;
        }
        catch (OperationCanceledException) { }

        _setupWatcher.Stop();
        _reader.Disconnect();

        _cts.Dispose();
        _cts = null;

        UpdateStatus(s =>
        {
            s.State = AgentState.Stopped;
            s.CurrentHz = 0;
        });
    }

    private async Task RunCaptureLoopAsync(CancellationToken ct)
    {
        double periodMs = 1000.0 / Math.Max(1, _config.TargetFrequencyHz);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(periodMs));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct)) break;

                // 1. Check reader connection
                if (!_reader.IsConnected)
                {
                    _reader.Connect();
                    if (!_reader.IsConnected)
                    {
                        UpdateStatus(s =>
                        {
                            s.GameLink = _config.DataSource == DataSourceType.LiveMemory
                                ? GameLinkState.Searching
                                : GameLinkState.Disconnected;
                            s.CurrentHz = 0;
                        });
                        continue;
                    }
                }

                // 2. Read Graphics state first to evaluate session and pit flags
                if (!_reader.TryReadGraphics(out var graphics))
                {
                    UpdateStatus(s =>
                    {
                        s.GameLink = GameLinkState.Searching;
                        s.SessionPhase = GameSessionPhase.Unknown;
                    });
                    continue;
                }

                var phase = DeterminePhase(in graphics);

                // Read Static data to know current car and track
                _reader.TryReadStatic(out var staticData);
                string car = !string.IsNullOrWhiteSpace(staticData.CarModel) ? staticData.CarModel : _status.CurrentCarModel;
                string trk = !string.IsNullOrWhiteSpace(staticData.Track) ? staticData.Track : _status.CurrentTrack;

                // If car/track changed, auto-query setups watcher for active setup
                if (!string.IsNullOrEmpty(car) && car != _lastQueriedCar)
                {
                    _lastQueriedCar = car;
                    _ = _setupWatcher.TryLoadSetupForCarAndTrackAsync(car, trk);
                }

                // Always read Physics for live input monitoring (steer angle, pedals)
                _reader.TryReadPhysics(out var physics);
                float steerDegrees = physics.SteerAngle * (180.0f / MathF.PI);

                // 3. Adaptive Throttling
                bool transmit = _throttlePolicy.ShouldTransmit(
                    graphics.Status,
                    graphics.IsInPitLane,
                    out var throttleMode);

                _capturedTotal++;
                CountHz();

                if (!transmit)
                {
                    _droppedTotal++;
                    UpdateThrottledStatus(throttleMode, graphics, phase, car, trk, steerDegrees);
                    continue;
                }

                // 5. Map to lightweight telemetry payload
                var payload = TelemetryMapper.MapToPayload(in physics, in graphics, in staticData);

                // 6. Push to lock-free ring buffer
                _ringBuffer.TryWrite(payload);

                // 7. Fire local telemetry event for UI mini-HUD
                FrameDispatched?.Invoke(payload);

                // Update live status
                UpdateLiveStatus(throttleMode, payload, phase, steerDegrees);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                UpdateStatus(s => s.LastError = ex.Message);
            }
        }
    }

    private async Task RunDispatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_ringBuffer.TryRead(out var payload) && payload != null)
                {
                    bool sent = await _ingestionClient.SendTelemetryAsync(payload, ct);
                    if (sent)
                    {
                        _sentTotal++;
                    }
                    else
                    {
                        _droppedTotal++;
                    }
                }
                else
                {
                    // Buffer empty, yield briefly
                    await Task.Delay(2, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(10, ct);
            }
        }
    }

    private async Task RunHealthCheckLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct)) break;

                await _ingestionClient.PingServerAsync(ct);
                UpdateStatus(s =>
                {
                    s.BackendLink = _ingestionClient.IsConnected
                        ? BackendLinkState.Connected
                        : BackendLinkState.Disconnected;
                    s.LastServerLatencyMs = _ingestionClient.LastLatencyMs;
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Suppress background health check errors
            }
        }
    }

    private void OnSetupDetected(SetupPayload payload)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                bool synced = await _ingestionClient.SendSetupAsync(payload);
                if (synced)
                {
                    UpdateStatus(s =>
                    {
                        s.LastSyncedSetupName = payload.FileName;
                        s.LastSyncedSetupTime = payload.Timestamp;
                    });
                    SetupSynced?.Invoke(payload);
                }
            }
            catch
            {
                // Suppress background setup sync error
            }
        });
    }

    private void CountHz()
    {
        _hzCounter++;
        if (_hzStopwatch.ElapsedMilliseconds >= 1000)
        {
            _effectiveHz = Math.Round(_hzCounter * 1000.0 / _hzStopwatch.ElapsedMilliseconds, 1);
            _hzCounter = 0;
            _hzStopwatch.Restart();
        }
    }

    private static GameSessionPhase DeterminePhase(in SPageFileGraphic graphics)
    {
        if (graphics.Status == 0) return GameSessionPhase.InLobbyOrMenu;
        if (graphics.IsSetupMenuVisible == 1) return GameSessionPhase.InGarageSetup;
        if (graphics.IsInPitLane == 1) return GameSessionPhase.InPitLane;
        if (graphics.Status == 2) return GameSessionPhase.LiveOnTrack;
        if (graphics.Status == 3) return GameSessionPhase.SessionPaused;
        if (graphics.Status == 1) return GameSessionPhase.Replay;
        return GameSessionPhase.InLobbyOrMenu;
    }

    private void UpdateLiveStatus(ThrottleMode mode, TelemetryPayload payload, GameSessionPhase phase, float steerDegrees)
    {
        var stats = _throttlePolicy.GetStatistics();
        UpdateStatus(s =>
        {
            s.State = mode == ThrottleMode.ThrottledPitLane ? AgentState.RunningThrottled : AgentState.RunningLive;
            s.GameLink = _config.DataSource == DataSourceType.LiveMemory ? GameLinkState.Connected : GameLinkState.MockEmulation;
            s.SessionPhase = phase;
            s.SteerAngleDegrees = steerDegrees;
            s.CurrentHz = _effectiveHz;
            s.TotalPacketsCaptured = _capturedTotal;
            s.TotalPacketsSent = _sentTotal;
            s.TotalPacketsDropped = _droppedTotal;
            s.BandwidthSavingsPercentage = stats.SavingsPercentage;
            s.CurrentCarModel = payload.CarModel;
            s.CurrentTrack = payload.Track;
            s.SetupsWatchDirectory = _setupWatcher.WatchDirectory;
        });
    }

    private void UpdateThrottledStatus(ThrottleMode mode, in SPageFileGraphic graphics, GameSessionPhase phase, string car, string trk, float steerDegrees)
    {
        var stats = _throttlePolicy.GetStatistics();
        UpdateStatus(s =>
        {
            s.State = mode == ThrottleMode.StandbyHeartbeat ? AgentState.StandbyHeartbeat : AgentState.RunningThrottled;
            s.GameLink = _config.DataSource == DataSourceType.LiveMemory ? GameLinkState.Connected : GameLinkState.MockEmulation;
            s.SessionPhase = phase;
            s.SteerAngleDegrees = steerDegrees;
            s.CurrentHz = _effectiveHz;
            s.TotalPacketsCaptured = _capturedTotal;
            s.TotalPacketsSent = _sentTotal;
            s.TotalPacketsDropped = _droppedTotal;
            s.BandwidthSavingsPercentage = stats.SavingsPercentage;
            if (!string.IsNullOrEmpty(car)) s.CurrentCarModel = car;
            if (!string.IsNullOrEmpty(trk)) s.CurrentTrack = trk;
            s.SetupsWatchDirectory = _setupWatcher.WatchDirectory;
        });
    }

    private void UpdateStatus(Action<AgentStatus> updater)
    {
        lock (_statusLock)
        {
            updater(_status);
            StatusChanged?.Invoke(_status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _setupWatcher.SetupDetected -= OnSetupDetected;
        _setupWatcher.Dispose();
        _ingestionClient.Dispose();
        _liveReader.Dispose();
        _mockEngine.Dispose();
        _traceReplayer.Dispose();
    }
}
