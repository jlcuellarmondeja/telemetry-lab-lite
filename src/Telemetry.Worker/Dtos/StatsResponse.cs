using Telemetry.Application.Stats;

namespace Telemetry.Worker.Dtos;

public sealed record StatsResponse(
    PipelineStatsSnapshot Totals,
    int DeviceCount,
    IReadOnlyDictionary<string, DeviceStats> Devices);
