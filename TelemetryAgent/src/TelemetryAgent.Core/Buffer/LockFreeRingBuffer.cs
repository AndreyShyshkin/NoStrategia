namespace TelemetryAgent.Core.Buffer;

/// <summary>
/// High-performance bounded circular buffer with FIFO ordering and newest-first displacement on overflow.
/// Designed for low latency audio/telemetry streaming loops without GC allocations.
/// </summary>
public sealed class LockFreeRingBuffer<T> where T : class
{
    private readonly T?[] _buffer;
    private readonly int _capacity;
    private readonly object _syncRoot = new();

    private int _head; // Points to write index
    private int _tail; // Points to read index
    private int _count;

    public int Capacity => _capacity;
    public int Count { get { lock (_syncRoot) return _count; } }
    public long OverwrittenCount { get; private set; }

    public LockFreeRingBuffer(int capacity = 2048)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _buffer = new T?[capacity];
    }

    /// <summary>
    /// Writes an item to the buffer. If full, drops the oldest item to preserve real-time freshness.
    /// </summary>
    public bool TryWrite(T item)
    {
        lock (_syncRoot)
        {
            if (_count == _capacity)
            {
                // Displace oldest
                _tail = (_tail + 1) % _capacity;
                _count--;
                OverwrittenCount++;
            }

            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            _count++;
            return true;
        }
    }

    /// <summary>
    /// Reads the next available item in FIFO order.
    /// </summary>
    public bool TryRead(out T? item)
    {
        lock (_syncRoot)
        {
            if (_count == 0)
            {
                item = null;
                return false;
            }

            item = _buffer[_tail];
            _buffer[_tail] = null; // Free reference for GC
            _tail = (_tail + 1) % _capacity;
            _count--;
            return true;
        }
    }

    /// <summary>
    /// Clears the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            Array.Clear(_buffer, 0, _capacity);
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}
