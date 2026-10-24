using Microsoft.Extensions.Logging;
using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;
using Telemetry.Domain.Validation;

namespace Telemetry.Application.Processing;

public sealed class TelemetryProcessor(
    IDeduplicationStore deduplication,
    IDeviceStatsStore deviceStats,
    TimeProvider timeProvider,
    ILogger<TelemetryProcessor> logger) : ITelemetryProcessor
{
    public Task<ProcessingResult> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        // 1. Validate first: invalid events never take a slot in the dedupe store.
        var error = TelemetryValidator.Validate(telemetryEvent, timeProvider.GetUtcNow());
        if (error is not null)
            return Task.FromResult(ProcessingResult.Invalid(error));

        // 2. Same eventId already taken → don't count it again.
        if (!deduplication.TryBegin(telemetryEvent.EventId))
            return Task.FromResult(ProcessingResult.Duplicate);

        try
        {
            // 3. Block: persistence with retries. Block: per-device counter.

            // 4. Count it. Only valid, non-duplicate events reach this line → counted exactly once.
            deviceStats.RecordValidEvent(telemetryEvent.DeviceId, telemetryEvent.Timestamp);

            logger.LogDebug("Processed {DeviceId} {EventId}", telemetryEvent.DeviceId, telemetryEvent.EventId);
            return Task.FromResult(ProcessingResult.Ok);
        }
        catch
        {
            // Failed → release the eventId so a retry or redelivery can process it.
            deduplication.Release(telemetryEvent.EventId);
            throw;
        }
    }
}
