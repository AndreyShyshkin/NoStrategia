using System.Text.Json;
using TelemetryAgent.Core.Models;
using TelemetryAgent.Core.Models.Memory;
using TelemetryAgent.Core.SharedMemory;

namespace TelemetryAgent.Core.Mock;

/// <summary>
/// Replays recorded NDJSON telemetry streams at authentic 60/100 Hz cadence
/// or accelerated speeds, enabling repeatable autonomous integration testing.
/// </summary>
public sealed class TraceReplayer : ITelemetryReader
{
    private readonly List<TelemetryPayload> _frames = [];
    private int _currentIndex;
    private readonly object _lock = new();
    private bool _isConnected;
    private bool _loopPlayback = true;

    public bool IsConnected => _isConnected && _frames.Count > 0;
    public string SourceName => $"NDJSON Trace Replay ({_frames.Count} frames loaded)";
    public int TotalFrames => _frames.Count;
    public int CurrentIndex { get { lock (_lock) return _currentIndex; } }
    public bool LoopPlayback { get => _loopPlayback; set => _loopPlayback = value; }

    public event Action<TelemetryPayload>? FrameDispatched;

    public async Task<int> LoadTraceAsync(string ndjsonPath, CancellationToken ct = default)
    {
        if (!File.Exists(ndjsonPath))
            throw new FileNotFoundException("NDJSON trace file not found", ndjsonPath);

        lock (_lock)
        {
            _frames.Clear();
            _currentIndex = 0;
        }

        using var stream = new FileStream(ndjsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var payload = JsonSerializer.Deserialize<TelemetryPayload>(line);
                if (payload != null)
                {
                    _frames.Add(payload);
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        _isConnected = _frames.Count > 0;
        return _frames.Count;
    }

    public void LoadFrames(IEnumerable<TelemetryPayload> frames)
    {
        lock (_lock)
        {
            _frames.Clear();
            _frames.AddRange(frames);
            _currentIndex = 0;
            _isConnected = _frames.Count > 0;
        }
    }

    public bool Connect()
    {
        _isConnected = _frames.Count > 0;
        return _isConnected;
    }

    public void Disconnect()
    {
        _isConnected = false;
    }

    public bool TryReadNextFrame(out TelemetryPayload? frame)
    {
        frame = null;
        lock (_lock)
        {
            if (_frames.Count == 0 || !_isConnected) return false;

            if (_currentIndex >= _frames.Count)
            {
                if (_loopPlayback)
                {
                    _currentIndex = 0;
                }
                else
                {
                    return false;
                }
            }

            frame = _frames[_currentIndex];
            _currentIndex++;
        }

        FrameDispatched?.Invoke(frame);
        return true;
    }

    public bool TryReadPhysics(out SPageFilePhysics physics)
    {
        physics = default;
        if (!TryReadNextFrame(out var payload) || payload == null) return false;

        physics = new SPageFilePhysics
        {
            PacketId = payload.PacketId,
            SpeedKmh = payload.SpeedKmh,
            Gear = payload.Gear,
            Rpm = payload.Rpm,
            SteerAngle = payload.SteerAngle,
            Gas = payload.Gas,
            Brake = payload.Brake,
            Clutch = payload.Clutch,
            Fuel = payload.Fuel,
            TC = payload.TC,
            Abs = payload.Abs,
            EngineMap = payload.EngineMap,
            Velocity = payload.Velocity,
            AccG = payload.AccG,
            LocalAngularVel = payload.LocalAngularVel,
            WheelSlip = payload.WheelSlip,
            WheelsPressure = payload.WheelsPressure,
            WheelAngularSpeed = payload.WheelAngularSpeed,
            TyreCoreTemperature = payload.TyreCoreTemperature,
            BrakeTemp = payload.BrakeTemp,
            SuspensionTravel = payload.SuspensionTravel,
            SlipRatio = payload.SlipRatio,
            SlipAngle = payload.SlipAngle
        };

        return true;
    }

    public bool TryReadGraphics(out SPageFileGraphic graphics)
    {
        graphics = default;
        lock (_lock)
        {
            int idx = Math.Max(0, Math.Min(_currentIndex - 1, _frames.Count - 1));
            if (_frames.Count == 0) return false;

            var p = _frames[idx];
            graphics = new SPageFileGraphic
            {
                PacketId = p.PacketId,
                Status = p.Status,
                Session = p.SessionType,
                ICurrentTime = p.ICurrentTime,
                ILastTime = p.ILastTime,
                IBestTime = p.IBestTime,
                CompletedLaps = p.CompletedLaps,
                NormalizedCarPosition = p.NormalizedCarPosition,
                CurrentSectorIndex = p.CurrentSectorIndex,
                IsInPitLane = p.IsInPitLane,
                IsInPit = p.IsInPit,
                TyreCompound = p.TyreCompound
            };
            return true;
        }
    }

    public bool TryReadStatic(out SPageFileStatic staticData)
    {
        staticData = new SPageFileStatic
        {
            CarModel = _frames.Count > 0 ? _frames[0].CarModel : "amg_gt3_evo",
            Track = _frames.Count > 0 ? _frames[0].Track : "spa",
            SectorCount = 3,
            TrackSplineLength = 7004.0f
        };
        return true;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
