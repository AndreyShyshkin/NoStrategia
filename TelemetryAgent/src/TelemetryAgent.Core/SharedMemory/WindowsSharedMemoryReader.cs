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

    public unsafe bool TryReadPhysics(out SPageFilePhysics physics)
    {
        physics = default;
        if (!_isConnected || _physicsAccessor == null) return false;

        try
        {
            byte* pointer = null;
            _physicsAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                if (pointer != null)
                {
                    physics = Marshal.PtrToStructure<SPageFilePhysics>((IntPtr)pointer);
                    return true;
                }
            }
            finally
            {
                _physicsAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch
        {
            _isConnected = false;
        }

        return false;
    }

    public unsafe bool TryReadGraphics(out SPageFileGraphic graphics)
    {
        graphics = default;
        if (!_isConnected || _graphicsAccessor == null) return false;

        try
        {
            byte* pointer = null;
            _graphicsAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                if (pointer != null)
                {
                    graphics = Marshal.PtrToStructure<SPageFileGraphic>((IntPtr)pointer);
                    return true;
                }
            }
            finally
            {
                _graphicsAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch
        {
            _isConnected = false;
        }

        return false;
    }

    public unsafe bool TryReadStatic(out SPageFileStatic staticData)
    {
        staticData = default;
        if (!_isConnected || _staticAccessor == null) return false;

        try
        {
            byte* pointer = null;
            _staticAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                if (pointer != null)
                {
                    staticData = Marshal.PtrToStructure<SPageFileStatic>((IntPtr)pointer);
                    return true;
                }
            }
            finally
            {
                _staticAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch
        {
            // Non-critical, static can be missing on initial boot
        }

        return false;
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
