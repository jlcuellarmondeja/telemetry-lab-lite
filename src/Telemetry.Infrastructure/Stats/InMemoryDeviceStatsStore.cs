using System.Collections.Concurrent;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Stats;

namespace Telemetry.Infrastructure.Stats;

public sealed class InMemoryDeviceStatsStore : IDeviceStatsStore
{
    private readonly ConcurrentDictionary<string, DeviceStats> _devices = new();

    public void RecordValidEvent(string deviceId, DateTimeOffset timestamp)
    {
        // NOT "_devices[deviceId].Count++" (read-add-write race).
        // AddOrUpdate swaps the whole value atomically; if two threads collide, one retries.
        _devices.AddOrUpdate(
            deviceId,
            _ => new DeviceStats(1, timestamp),
            (_, current) => current with
            {
                ValidEvents = current.ValidEvents + 1,
                LastEventAt = timestamp > current.LastEventAt ? timestamp : current.LastEventAt
            });
    }

    // ToArray takes a consistent point-in-time copy, so /stats never reads while others write.
    public IReadOnlyDictionary<string, DeviceStats> Snapshot()
        => _devices.ToArray().ToDictionary(kv => kv.Key, kv => kv.Value);
}
