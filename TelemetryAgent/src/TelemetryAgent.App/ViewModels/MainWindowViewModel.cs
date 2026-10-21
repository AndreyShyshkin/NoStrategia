using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using TelemetryAgent.Core.Mock;
using TelemetryAgent.Core.Models;
using TelemetryAgent.Core.Services;
using TelemetryAgent.Core.Setups;

namespace TelemetryAgent.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly TelemetryAgentCoordinator _coordinator;
    private readonly TraceRecorder _traceRecorder = new();

    // 1. Agent Status Pillar
    private string _agentStatusBadge = "INITIALIZING";
    private IBrush _agentStatusColor = new SolidColorBrush(Color.Parse("#888888"));
    private string _agentDetails = "Starting background capture loop...";
    private string _effectiveHzText = "0.0 Hz";
    private string _savingsText = "Bandwidth Saved: 0%";

    // 2. Game Status Pillar
    private string _gameStatusBadge = "SEARCHING";
    private IBrush _gameStatusColor = new SolidColorBrush(Color.Parse("#FF9100"));
    private string _gameDetails = "Waiting for Assetto Corsa Competizione (MMF)...";
    private string _carAndTrackText = "No Game Process Detected";

    // 3. Server Status Pillar
    private string _serverStatusBadge = "CONNECTING";
    private IBrush _serverStatusColor = new SolidColorBrush(Color.Parse("#888888"));
    private string _serverDetails = "Ingestion Gateway: 127.0.0.1:4000";
    private string _serverPingText = "Ping: -- ms";
    private string _packetsCounterText = "Sent: 0 | Dropped: 0";

    // 4. Setup Interceptor
    private string _setupWatcherBadge = "ACTIVE";
    private string _lastSetupSynced = "None (Watching Documents/ACC/Setups)";
    private string _lastSetupHash = "--";
    private string _watchDirectoryText = "Scanning Setups...";

    // Live Mini-HUD
    private string _speedText = "0";
    private string _gearText = "N";
    private string _rpmText = "0 RPM";
    private double _rpmProgress = 0;
    private double _throttleProgress = 0;
    private double _brakeProgress = 0;
    private string _steerText = "0.0° CENTER";
    private float _steerAngleDegrees = 0f;
    private double _steerProgressLeft = 0.0;
    private double _steerProgressRight = 0.0;
    private string _lapTimeText = "00:00.000";
    private string _sectorText = "Sector: -";
    private string _tyrePressuresText = "FL: -- | FR: -- | RL: -- | RR: --";
    private string _tyreTempsText = "Tyre Temps: -- °C";
    private int _isUiFramePending;
    private TelemetryPayload? _latestFrame;
    private float _maxObservedSteer = 57.3f;
    private double _maxCarRpm = 7500.0;

    // Mode indicator (Dev vs Production)
#if PROD_MODE
    public bool IsDevMode => false;
    public string BuildFlavorText => "PRODUCTION BUILD";
#else
    public bool IsDevMode => true;
    public string BuildFlavorText => "DEV / MOCK BUILD";
#endif

    // Controls & Settings
#if PROD_MODE
    private bool _isMockMode = false;
    private bool _isLiveMode = true;
#else
    private bool _isMockMode = true;
    private bool _isLiveMode = false;
#endif
    private bool _isRecording = false;
    private string _recordButtonText = "⏺ Record NDJSON";
    private bool _isSettingsOpen = false;

    // Config form
    private string _ingestionUrl = "http://127.0.0.1:4000/api/v1/telemetry";
    private string _setupSyncUrl = "http://127.0.0.1:4000/api/v1/setups/sync";
    private int _targetFrequencyHz = 60;
    private bool _enableAdaptiveThrottling = true;
    private string _setupsDirectory = string.Empty;
    private bool _minimizeToTrayOnClose = true;
    private string _feedbackMessage = "Ready";

    public string AgentStatusBadge { get => _agentStatusBadge; set => SetProperty(ref _agentStatusBadge, value); }
    public IBrush AgentStatusColor { get => _agentStatusColor; set => SetProperty(ref _agentStatusColor, value); }
    public string AgentDetails { get => _agentDetails; set => SetProperty(ref _agentDetails, value); }
    public string EffectiveHzText { get => _effectiveHzText; set => SetProperty(ref _effectiveHzText, value); }
    public string SavingsText { get => _savingsText; set => SetProperty(ref _savingsText, value); }

    public string GameStatusBadge { get => _gameStatusBadge; set => SetProperty(ref _gameStatusBadge, value); }
    public IBrush GameStatusColor { get => _gameStatusColor; set => SetProperty(ref _gameStatusColor, value); }
    public string GameDetails { get => _gameDetails; set => SetProperty(ref _gameDetails, value); }
    public string CarAndTrackText { get => _carAndTrackText; set => SetProperty(ref _carAndTrackText, value); }

    public string ServerStatusBadge { get => _serverStatusBadge; set => SetProperty(ref _serverStatusBadge, value); }
    public IBrush ServerStatusColor { get => _serverStatusColor; set => SetProperty(ref _serverStatusColor, value); }
    public string ServerDetails { get => _serverDetails; set => SetProperty(ref _serverDetails, value); }
    public string ServerPingText { get => _serverPingText; set => SetProperty(ref _serverPingText, value); }
    public string PacketsCounterText { get => _packetsCounterText; set => SetProperty(ref _packetsCounterText, value); }

    public string SetupWatcherBadge { get => _setupWatcherBadge; set => SetProperty(ref _setupWatcherBadge, value); }
    public string LastSetupSynced { get => _lastSetupSynced; set => SetProperty(ref _lastSetupSynced, value); }
    public string LastSetupHash { get => _lastSetupHash; set => SetProperty(ref _lastSetupHash, value); }
    public string WatchDirectoryText { get => _watchDirectoryText; set => SetProperty(ref _watchDirectoryText, value); }

    public string SpeedText { get => _speedText; set => SetProperty(ref _speedText, value); }
    public string GearText { get => _gearText; set => SetProperty(ref _gearText, value); }
    public string RpmText { get => _rpmText; set => SetProperty(ref _rpmText, value); }
    public double RpmProgress { get => _rpmProgress; set => SetProperty(ref _rpmProgress, value); }
    public double ThrottleProgress { get => _throttleProgress; set => SetProperty(ref _throttleProgress, value); }
    public double BrakeProgress { get => _brakeProgress; set => SetProperty(ref _brakeProgress, value); }
    public string SteerText { get => _steerText; set => SetProperty(ref _steerText, value); }
    public float SteerAngleDegrees { get => _steerAngleDegrees; set => SetProperty(ref _steerAngleDegrees, value); }
    public double SteerProgressLeft { get => _steerProgressLeft; set => SetProperty(ref _steerProgressLeft, value); }
    public double SteerProgressRight { get => _steerProgressRight; set => SetProperty(ref _steerProgressRight, value); }
    public string LapTimeText { get => _lapTimeText; set => SetProperty(ref _lapTimeText, value); }
    public string SectorText { get => _sectorText; set => SetProperty(ref _sectorText, value); }
    public string TyrePressuresText { get => _tyrePressuresText; set => SetProperty(ref _tyrePressuresText, value); }
    public string TyreTempsText { get => _tyreTempsText; set => SetProperty(ref _tyreTempsText, value); }

    public bool IsMockMode { get => _isMockMode; set => SetProperty(ref _isMockMode, value); }
    public bool IsLiveMode { get => _isLiveMode; set => SetProperty(ref _isLiveMode, value); }
    public bool IsRecording { get => _isRecording; set => SetProperty(ref _isRecording, value); }
    public string RecordButtonText { get => _recordButtonText; set => SetProperty(ref _recordButtonText, value); }
    public bool IsSettingsOpen { get => _isSettingsOpen; set => SetProperty(ref _isSettingsOpen, value); }

    public string IngestionUrl { get => _ingestionUrl; set => SetProperty(ref _ingestionUrl, value); }
    public string SetupSyncUrl { get => _setupSyncUrl; set => SetProperty(ref _setupSyncUrl, value); }
    public int TargetFrequencyHz { get => _targetFrequencyHz; set => SetProperty(ref _targetFrequencyHz, value); }
    public bool EnableAdaptiveThrottling { get => _enableAdaptiveThrottling; set => SetProperty(ref _enableAdaptiveThrottling, value); }
    public string SetupsDirectory { get => _setupsDirectory; set => SetProperty(ref _setupsDirectory, value); }
    public bool MinimizeToTrayOnClose { get => _minimizeToTrayOnClose; set => SetProperty(ref _minimizeToTrayOnClose, value); }
    public string FeedbackMessage { get => _feedbackMessage; set => SetProperty(ref _feedbackMessage, value); }

    public MainWindowViewModel()
    {
        if (Program.IsRunningUnderWine())
        {
            _minimizeToTrayOnClose = false;
        }

#if PROD_MODE
        var config = new AgentConfiguration
        {
            DataSource = DataSourceType.LiveMemory, // Strictly live memory for production
            TargetFrequencyHz = 60,
            EnableAdaptiveThrottling = true,
            IngestionUrl = _ingestionUrl,
            SetupSyncUrl = _setupSyncUrl,
            MinimizeToTrayOnClose = _minimizeToTrayOnClose
        };
#else
        var config = new AgentConfiguration
        {
            DataSource = DataSourceType.MockEngine, // Starts in mock mode for dev/benchmarking
            TargetFrequencyHz = 60,
            EnableAdaptiveThrottling = true,
            IngestionUrl = _ingestionUrl,
            SetupSyncUrl = _setupSyncUrl,
            MinimizeToTrayOnClose = _minimizeToTrayOnClose
        };
#endif

        _coordinator = new TelemetryAgentCoordinator(config);
        _setupsDirectory = _coordinator.SetupWatcher.WatchDirectory;
        _watchDirectoryText = _coordinator.SetupWatcher.WatchDirectory;
        _coordinator.StatusChanged += OnCoordinatorStatusChanged;
        _coordinator.FrameDispatched += OnFrameDispatched;
        _coordinator.SetupSynced += OnSetupSynced;

        _ = _coordinator.StartAsync();
        _ = RefreshSetupsAsync();
    }

    private void OnCoordinatorStatusChanged(AgentStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // 1. Agent status
            EffectiveHzText = $"{status.CurrentHz:F1} Hz";
            SavingsText = $"Bandwidth Saved: {status.BandwidthSavingsPercentage:F1}%";

            switch (status.State)
            {
                case AgentState.RunningLive:
                    AgentStatusBadge = "ACTIVE (FULL RATE)";
                    AgentStatusColor = new SolidColorBrush(Color.Parse("#00E676")); // Neon green
                    AgentDetails = "Transmitting at high frequency (60-100 Hz)";
                    break;
                case AgentState.RunningThrottled:
                    AgentStatusBadge = "THROTTLED (PIT LANE)";
                    AgentStatusColor = new SolidColorBrush(Color.Parse("#FF9100")); // Amber
                    AgentDetails = "Downsampled to 10 Hz (Pit lane state)";
                    break;
                case AgentState.StandbyHeartbeat:
                    AgentStatusBadge = "STANDBY (HEARTBEAT)";
                    AgentStatusColor = new SolidColorBrush(Color.Parse("#2979FF")); // Blue
                    AgentDetails = "0.5 Hz heartbeat (Pause / Menu state)";
                    break;
                case AgentState.Starting:
                    AgentStatusBadge = "INITIALIZING";
                    AgentStatusColor = new SolidColorBrush(Color.Parse("#FFD600"));
                    break;
                default:
                    AgentStatusBadge = "STOPPED";
                    AgentStatusColor = new SolidColorBrush(Color.Parse("#888888"));
                    break;
            }

            // 2. Game status (with explicit Lobby / Garage / Track session states)
            switch (status.GameLink)
            {
                case GameLinkState.Connected:
                    switch (status.SessionPhase)
                    {
                        case GameSessionPhase.InLobbyOrMenu:
                            GameStatusBadge = "IN LOBBY / MENU";
                            GameStatusColor = new SolidColorBrush(Color.Parse("#FFD600")); // Gold / Yellow
                            GameDetails = "Game active, waiting in lobby or main menu";
                            CarAndTrackText = string.IsNullOrEmpty(status.CurrentCarModel)
                                ? "Lobby / Car Selection"
                                : $"{status.CurrentCarModel} (In Lobby)";
                            break;

                        case GameSessionPhase.InGarageSetup:
                            GameStatusBadge = "GARAGE / SETUP";
                            GameStatusColor = new SolidColorBrush(Color.Parse("#00E5FF")); // Cyan
                            GameDetails = "Tuning car setup in pit box menu";
                            CarAndTrackText = $"{status.CurrentCarModel} @ {status.CurrentTrack}";
                            break;

                        case GameSessionPhase.InPitLane:
                            GameStatusBadge = "PIT LANE (10 HZ)";
                            GameStatusColor = new SolidColorBrush(Color.Parse("#FF9100")); // Orange
                            GameDetails = "Pit limiter active / stationary in pit box";
                            CarAndTrackText = $"{status.CurrentCarModel} @ {status.CurrentTrack}";
                            break;

                        case GameSessionPhase.SessionPaused:
                            GameStatusBadge = "SESSION PAUSED";
                            GameStatusColor = new SolidColorBrush(Color.Parse("#2979FF")); // Blue
                            GameDetails = "Game paused, telemetry idling at 0.5 Hz";
                            CarAndTrackText = $"{status.CurrentCarModel} @ {status.CurrentTrack}";
                            break;

                        case GameSessionPhase.Replay:
                            GameStatusBadge = "REPLAY MODE";
                            GameStatusColor = new SolidColorBrush(Color.Parse("#AA00FF")); // Purple
                            GameDetails = "Viewing session replay";
                            CarAndTrackText = $"{status.CurrentCarModel} @ {status.CurrentTrack}";
                            break;

                        case GameSessionPhase.LiveOnTrack:
                        default:
                            GameStatusBadge = "LIVE ON TRACK";
                            GameStatusColor = new SolidColorBrush(Color.Parse("#00E676")); // Neon green
                            GameDetails = "Assetto Corsa Competizione (Live driving)";
                            CarAndTrackText = $"{status.CurrentCarModel} @ {status.CurrentTrack}";
                            break;
                    }
                    break;

                case GameLinkState.MockEmulation:
                    GameStatusBadge = "MOCK SIMULATOR";
                    GameStatusColor = new SolidColorBrush(Color.Parse("#00E5FF")); // Cyan
                    GameDetails = "Autonomous GT3 Simulation Engine (Spa)";
                    CarAndTrackText = "Mercedes-AMG GT3 Evo @ Circuit de Spa";
                    break;

                default:
                    GameStatusBadge = "SEARCHING";
                    GameStatusColor = new SolidColorBrush(Color.Parse("#FF9100"));
                    GameDetails = "Scanning Windows/Wine Shared Memory (acpmf_*)";
                    CarAndTrackText = "Waiting for game process...";
                    break;
            }

            // 3. Backend status
            switch (status.BackendLink)
            {
                case BackendLinkState.Connected:
                    ServerStatusBadge = "ONLINE";
                    ServerStatusColor = new SolidColorBrush(Color.Parse("#00E676"));
                    ServerDetails = $"Ingestion: {_coordinator.Configuration.IngestionUrl}";
                    ServerPingText = $"Ping: {status.LastServerLatencyMs:F1} ms";
                    break;
                default:
                    ServerStatusBadge = "OFFLINE";
                    ServerStatusColor = new SolidColorBrush(Color.Parse("#FF1744"));
                    ServerDetails = "Connecting to Ingestion Gateway...";
                    ServerPingText = "Ping: --";
                    break;
            }

            PacketsCounterText = $"Sent: {status.TotalPacketsSent:N0} | Dropped: {status.TotalPacketsDropped:N0}";

            if (!string.IsNullOrEmpty(status.SetupsWatchDirectory))
            {
                WatchDirectoryText = status.SetupsWatchDirectory;
            }

            if (!string.IsNullOrEmpty(status.LastSyncedSetupName))
            {
                LastSetupSynced = status.LastSyncedSetupName;
                LastSetupHash = status.LastSyncedSetupTime.HasValue
                    ? $"Synced at {status.LastSyncedSetupTime.Value:HH:mm:ss}"
                    : "--";
            }
        });
    }

    private void OnFrameDispatched(TelemetryPayload p)
    {
        // Record if active
        if (_isRecording)
        {
            _ = _traceRecorder.RecordFrameAsync(p);
        }

        _latestFrame = p;
        if (System.Threading.Interlocked.CompareExchange(ref _isUiFramePending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var frame = _latestFrame;
                    if (frame != null)
                    {
                        RenderMiniHud(frame);
                    }
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _isUiFramePending, 0);
                }
            });
        }
    }

    private void RenderMiniHud(TelemetryPayload p)
    {
        SpeedText = $"{Math.Round(p.SpeedKmh)}";
        GearText = p.Gear switch
        {
            0 => "R",
            1 => "N",
            > 1 => $"{p.Gear - 1}",
            _ => "N"
        };
        RpmText = $"{p.Rpm} RPM";

        // Dynamically track and adapt redline RPM to current car
        if (p.Rpm > _maxCarRpm) _maxCarRpm = p.Rpm;
        RpmProgress = Math.Clamp(p.Rpm / _maxCarRpm, 0.0, 1.0);

        ThrottleProgress = Math.Clamp(p.Gas, 0.0, 1.0);
        BrakeProgress = Math.Clamp(p.Brake, 0.0, 1.0);

        // Steering angle calculation and dynamic per-car lock angle
        float steerDeg = p.SteerAngle * (180.0f / MathF.PI);
        SteerAngleDegrees = steerDeg;

        if (Math.Abs(steerDeg) > _maxObservedSteer)
        {
            _maxObservedSteer = Math.Max(57.3f, Math.Abs(steerDeg));
        }

        float maxSteer = _maxObservedSteer > 10.0f ? _maxObservedSteer : 57.3f;

        if (steerDeg < -0.5f)
        {
            SteerText = $"◀ {Math.Abs(steerDeg):F1}° L";
            SteerProgressLeft = Math.Clamp(Math.Abs(steerDeg) / maxSteer, 0.0, 1.0);
            SteerProgressRight = 0.0;
        }
        else if (steerDeg > 0.5f)
        {
            SteerText = $"{steerDeg:F1}° R ▶";
            SteerProgressRight = Math.Clamp(steerDeg / maxSteer, 0.0, 1.0);
            SteerProgressLeft = 0.0;
        }
        else
        {
            SteerText = "0.0° CENTER";
            SteerProgressLeft = 0.0;
            SteerProgressRight = 0.0;
        }

        if (p.ICurrentTime > 0)
        {
            var ts = TimeSpan.FromMilliseconds(p.ICurrentTime);
            LapTimeText = $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }
        else
        {
            LapTimeText = "00:00.000";
        }

        SectorText = $"Sector {p.CurrentSectorIndex + 1} | Lap {p.CompletedLaps + 1}";

        if (p.WheelsPressure.Length >= 4)
        {
            TyrePressuresText = $"FL: {p.WheelsPressure[0]:F1} | FR: {p.WheelsPressure[1]:F1} | RL: {p.WheelsPressure[2]:F1} | RR: {p.WheelsPressure[3]:F1} PSI";
        }
        if (p.TyreCoreTemperature.Length >= 4)
        {
            TyreTempsText = $"Tyres: {p.TyreCoreTemperature[0]:F0}° / {p.TyreCoreTemperature[1]:F0}° / {p.TyreCoreTemperature[2]:F0}° / {p.TyreCoreTemperature[3]:F0}° C";
        }
    }

    private void OnSetupSynced(SetupPayload s)
    {
        Dispatcher.UIThread.Post(() =>
        {
            string displayName = !string.IsNullOrEmpty(s.Track) ? $"{s.Track} / {s.FileName}" : s.FileName;
            LastSetupSynced = displayName;
            string shortHash = s.Hash.Length >= 12 ? s.Hash[..12] : s.Hash;
            LastSetupHash = $"SHA-256: {shortHash}... (Synced)";
            FeedbackMessage = $"Setup synced: {displayName}";
        });
    }

    // Command Actions
    public void SwitchToMock()
    {
        if (!IsDevMode) return;
        IsMockMode = true;
        IsLiveMode = false;
        _coordinator.SwitchDataSource(DataSourceType.MockEngine);
        FeedbackMessage = "Switched to Autonomous Mock GT3 Engine (Spa-Francorchamps)";
    }

    public void SwitchToLive()
    {
        IsMockMode = false;
        IsLiveMode = true;
        _coordinator.SwitchDataSource(DataSourceType.LiveMemory);
        FeedbackMessage = "Switched to Live Windows/Wine Shared Memory (acpmf_*)";
    }

    public void InjectLockup()
    {
        if (!IsDevMode) return;
        _coordinator.AnomalyInjector.InjectAnomaly(AnomalyType.WheelLockup, durationTicks: 90);
        FeedbackMessage = "Injected Front-Left Wheel Lockup (90 ticks)";
    }

    public void InjectOversteer()
    {
        if (!IsDevMode) return;
        _coordinator.AnomalyInjector.InjectAnomaly(AnomalyType.OversteerSpin, durationTicks: 90);
        FeedbackMessage = "Injected Oversteer Spin Anomaly (90 ticks)";
    }

    public void InjectFuelAlert()
    {
        if (!IsDevMode) return;
        _coordinator.AnomalyInjector.InjectAnomaly(AnomalyType.FuelShortage, durationTicks: 120);
        FeedbackMessage = "Injected Fuel Reserve Emergency Alert (1.15L)";
    }

    public async Task GenerateTestSetupAsync()
    {
        if (!IsDevMode) return;
        try
        {
            string targetDir = string.IsNullOrEmpty(SetupsDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa Competizione", "Setups")
                : SetupsDirectory;

            string filePath = await SetupSampleGenerator.CreateTestSetupFileAsync(
                targetDir,
                carName: "amg_gt3_evo",
                track: "spa",
                setupName: $"telemetry_test_{DateTime.UtcNow:HHmmss}.json",
                frontArb: new Random().Next(1, 8),
                rearArb: new Random().Next(1, 6));

            FeedbackMessage = $"Created test setup: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"Setup test error: {ex.Message}";
        }
    }

    public async Task ToggleRecordingAsync()
    {
        if (!_isRecording)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NoStrategia",
                "Traces",
                $"gt3_trace_{DateTime.UtcNow:yyyyMMdd_HHmmss}.ndjson");

            await _traceRecorder.StartAsync(path);
            IsRecording = true;
            RecordButtonText = "⏹ Stop Recording";
            FeedbackMessage = $"Recording to: {Path.GetFileName(path)}";
        }
        else
        {
            long count = _traceRecorder.FramesRecorded;
            await _traceRecorder.StopAsync();
            IsRecording = false;
            RecordButtonText = "⏺ Record NDJSON";
            FeedbackMessage = $"Saved {count:N0} frames to NDJSON file.";
        }
    }

    public async Task RefreshSetupsAsync()
    {
        FeedbackMessage = "Scanning for ACC setups...";
        var found = await _coordinator.RefreshSetupsAsync();
        if (found != null)
        {
            string displayName = !string.IsNullOrEmpty(found.Track) ? $"{found.Track} / {found.FileName}" : found.FileName;
            LastSetupSynced = displayName;
            string shortHash = found.Hash.Length >= 12 ? found.Hash[..12] : found.Hash;
            LastSetupHash = $"SHA-256: {shortHash}... (Synced)";
            FeedbackMessage = $"Found & synced setup: {displayName}";
        }
        else
        {
            FeedbackMessage = $"No setup found in {_coordinator.SetupWatcher.WatchDirectory}";
        }
    }

    public void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    public void SaveSettings()
    {
        _coordinator.Configuration.IngestionUrl = IngestionUrl;
        _coordinator.Configuration.SetupSyncUrl = SetupSyncUrl;
        _coordinator.Configuration.TargetFrequencyHz = TargetFrequencyHz;
        _coordinator.Configuration.EnableAdaptiveThrottling = EnableAdaptiveThrottling;
        _coordinator.Configuration.SetupsDirectory = SetupsDirectory;
        _coordinator.Configuration.MinimizeToTrayOnClose = MinimizeToTrayOnClose;

        if (!string.IsNullOrWhiteSpace(SetupsDirectory) && SetupsDirectory != _coordinator.SetupWatcher.WatchDirectory)
        {
            _coordinator.SetupWatcher.SetDirectory(SetupsDirectory);
            WatchDirectoryText = SetupsDirectory;
            _ = RefreshSetupsAsync();
        }

        IsSettingsOpen = false;
        FeedbackMessage = "Settings saved successfully.";
    }

    public async ValueTask DisposeAsync()
    {
        if (_isRecording) await _traceRecorder.StopAsync();
        await _coordinator.DisposeAsync();
    }
}
