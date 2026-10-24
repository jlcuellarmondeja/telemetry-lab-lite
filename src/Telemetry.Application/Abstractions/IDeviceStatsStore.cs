using Telemetry.Application.Stats;

namespace Telemetry.Application.Abstractions;

// Per-device counters. In-memory today; could be Redis/DB tomorrow without touching Application.
public interface IDeviceStatsStore
{
    void RecordValidEvent(string deviceId, DateTimeOffset timestamp);
    IReadOnlyDictionary<string, DeviceStats> Snapshot();
}
