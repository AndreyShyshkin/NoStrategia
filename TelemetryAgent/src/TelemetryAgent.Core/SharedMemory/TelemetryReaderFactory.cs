namespace TelemetryAgent.Core.SharedMemory;

public static class TelemetryReaderFactory
{
    public static ITelemetryReader CreateLiveReader()
    {
        return new WindowsSharedMemoryReader();
    }
}
