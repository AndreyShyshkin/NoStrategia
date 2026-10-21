using TelemetryAgent.Core.Models.Memory;

namespace TelemetryAgent.Core.SharedMemory;

/// <summary>
/// Abstraction for accessing Assetto Corsa Competizione shared memory buffers.
/// Supports both native Windows MMF, Wine prefixes, and mock/virtual memory sources.
/// </summary>
public interface ITelemetryReader : IDisposable
{
    bool IsConnected { get; }
    string SourceName { get; }

    bool Connect();
    void Disconnect();

    bool TryReadPhysics(out SPageFilePhysics physics);
    bool TryReadGraphics(out SPageFileGraphic graphics);
    bool TryReadStatic(out SPageFileStatic staticData);
}
