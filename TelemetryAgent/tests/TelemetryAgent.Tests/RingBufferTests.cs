using TelemetryAgent.Core.Buffer;
using TelemetryAgent.Core.Models;
using Xunit;

namespace TelemetryAgent.Tests;

public class RingBufferTests
{
    [Fact]
    public void RingBuffer_MaintainsFifoOrder()
    {
        var buffer = new LockFreeRingBuffer<TelemetryPayload>(capacity: 10);

        for (int i = 0; i < 5; i++)
        {
            buffer.TryWrite(new TelemetryPayload { PacketId = i });
        }

        Assert.Equal(5, buffer.Count);

        for (int i = 0; i < 5; i++)
        {
            bool read = buffer.TryRead(out var item);
            Assert.True(read);
            Assert.NotNull(item);
            Assert.Equal(i, item.PacketId);
        }

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void RingBuffer_DisplacesOldestWhenCapacityExceeded()
    {
        var buffer = new LockFreeRingBuffer<TelemetryPayload>(capacity: 3);

        buffer.TryWrite(new TelemetryPayload { PacketId = 1 });
        buffer.TryWrite(new TelemetryPayload { PacketId = 2 });
        buffer.TryWrite(new TelemetryPayload { PacketId = 3 });
        // Overflow with 4th item -> should drop PacketId 1
        buffer.TryWrite(new TelemetryPayload { PacketId = 4 });

        Assert.Equal(3, buffer.Count);
        Assert.Equal(1, buffer.OverwrittenCount);

        buffer.TryRead(out var item1);
        buffer.TryRead(out var item2);
        buffer.TryRead(out var item3);

        Assert.Equal(2, item1?.PacketId);
        Assert.Equal(3, item2?.PacketId);
        Assert.Equal(4, item3?.PacketId);
    }
}
