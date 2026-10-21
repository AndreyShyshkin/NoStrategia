#pragma warning disable CA1416
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using TelemetryAgent.Core.Models.Memory;

namespace TelemetryAgent.Core.SharedMemory;

/// <summary>
/// Reads Assetto Corsa Competizione telemetry directly from Windows Named Shared Memory (MMF).
/// Works natively on Windows 10/11 and inside Wine / CrossOver bottles on macOS.
/// </summary>
public sealed class WindowsSharedMemoryReader : ITelemetryReader
{
    private MemoryMappedFile? _physicsMmf;
    private MemoryMappedViewAccessor? _physicsAccessor;

    private MemoryMappedFile? _graphicsMmf;
    private MemoryMappedViewAccessor? _graphicsAccessor;

    private MemoryMappedFile? _staticMmf;
    private MemoryMappedViewAccessor? _staticAccessor;

    private readonly byte[] _physicsBuffer = new byte[Marshal.SizeOf<SPageFilePhysics>()];
    private readonly byte[] _graphicsBuffer = new byte[Marshal.SizeOf<SPageFileGraphic>()];
    private readonly byte[] _staticBuffer = new byte[Marshal.SizeOf<SPageFileStatic>()];

    private bool _isConnected;
    private readonly object _syncLock = new();

    public bool IsConnected => _isConnected;
    public string SourceName => "Windows Named Shared Memory (MMF / Wine)";

    public bool Connect()
    {
        lock (_syncLock)
        {
            if (_isConnected) return true;

            try
            {
                _physicsMmf = TryOpenFile(MemoryConstants.PhysicsMmfName, MemoryConstants.FallbackPhysicsMmfName);
                _graphicsMmf = TryOpenFile(MemoryConstants.GraphicsMmfName, MemoryConstants.FallbackGraphicsMmfName);
                _staticMmf = TryOpenFile(MemoryConstants.StaticMmfName, MemoryConstants.FallbackStaticMmfName);

                if (_physicsMmf != null && _graphicsMmf != null)
                {
                    _physicsAccessor = _physicsMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    _graphicsAccessor = _graphicsMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    _staticAccessor = _staticMmf?.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                    _isConnected = true;
                    return true;
                }
            }
            catch (Exception)
            {
                Disconnect();
            }

            return false;
        }
    }

    private static MemoryMappedFile? TryOpenFile(string primaryName, string fallbackName)
    {
        if (!OperatingSystem.IsWindows())
        {
            // On non-Windows OS (unless running inside Wine where IsWindows() returns true for Win-x64 binaries)
            return null;
        }

        try
        {
            return MemoryMappedFile.OpenExisting(primaryName, MemoryMappedFileRights.Read);
        }
        catch (FileNotFoundException)
        {
            try
            {
                return MemoryMappedFile.OpenExisting(fallbackName, MemoryMappedFileRights.Read);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool TryReadPhysics(out SPageFilePhysics physics)
    {
        physics = default;
        if (!_isConnected || _physicsAccessor == null) return false;

        try
        {
            int structSize = _physicsBuffer.Length;
            int bytesToRead = (int)Math.Min(_physicsAccessor.Capacity, structSize);
            if (bytesToRead <= 0) return false;

            _physicsAccessor.ReadArray(0, _physicsBuffer, 0, bytesToRead);
            if (bytesToRead < structSize)
            {
                Array.Clear(_physicsBuffer, bytesToRead, structSize - bytesToRead);
            }

            var handle = GCHandle.Alloc(_physicsBuffer, GCHandleType.Pinned);
            try
            {
                physics = Marshal.PtrToStructure<SPageFilePhysics>(handle.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
        catch
        {
            _isConnected = false;
            return false;
        }
    }

    public bool TryReadGraphics(out SPageFileGraphic graphics)
    {
        graphics = default;
        if (!_isConnected || _graphicsAccessor == null) return false;

        try
        {
            int structSize = _graphicsBuffer.Length;
            int bytesToRead = (int)Math.Min(_graphicsAccessor.Capacity, structSize);
            if (bytesToRead <= 0) return false;

            _graphicsAccessor.ReadArray(0, _graphicsBuffer, 0, bytesToRead);
            if (bytesToRead < structSize)
            {
                Array.Clear(_graphicsBuffer, bytesToRead, structSize - bytesToRead);
            }

            var handle = GCHandle.Alloc(_graphicsBuffer, GCHandleType.Pinned);
            try
            {
                graphics = Marshal.PtrToStructure<SPageFileGraphic>(handle.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
        catch
        {
            _isConnected = false;
            return false;
        }
    }

    public bool TryReadStatic(out SPageFileStatic staticData)
    {
        staticData = default;
        if (!_isConnected || _staticAccessor == null) return false;

        try
        {
            int structSize = _staticBuffer.Length;
            int bytesToRead = (int)Math.Min(_staticAccessor.Capacity, structSize);
            if (bytesToRead <= 0) return false;

            _staticAccessor.ReadArray(0, _staticBuffer, 0, bytesToRead);
            if (bytesToRead < structSize)
            {
                Array.Clear(_staticBuffer, bytesToRead, structSize - bytesToRead);
            }

            var handle = GCHandle.Alloc(_staticBuffer, GCHandleType.Pinned);
            try
            {
                staticData = Marshal.PtrToStructure<SPageFileStatic>(handle.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
        catch
        {
            // Non-critical, static can be missing on initial boot
            return false;
        }
    }

    public void Disconnect()
    {
        lock (_syncLock)
        {
            _isConnected = false;

            _physicsAccessor?.Dispose();
            _physicsAccessor = null;
            _physicsMmf?.Dispose();
            _physicsMmf = null;

            _graphicsAccessor?.Dispose();
            _graphicsAccessor = null;
            _graphicsMmf?.Dispose();
            _graphicsMmf = null;

            _staticAccessor?.Dispose();
            _staticAccessor = null;
            _staticMmf?.Dispose();
            _staticMmf = null;
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
