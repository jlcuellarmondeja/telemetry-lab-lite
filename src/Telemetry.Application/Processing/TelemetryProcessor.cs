using Microsoft.Extensions.Logging;
using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;
using Telemetry.Domain.Validation;

namespace Telemetry.Application.Processing;

public sealed class TelemetryProcessor(
    IDeduplicationStore deduplication,
    ITelemetryPersistence persistence,
    IDeviceStatsStore deviceStats,
    TimeProvider timeProvider,
    ILogger<TelemetryProcessor> logger) : ITelemetryProcessor
{
    public async Task<ProcessingResult> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        // 1. Validate first: invalid events never take a slot in the dedupe store.
        var error = TelemetryValidator.Validate(telemetryEvent, timeProvider.GetUtcNow());
        if (error is not null)
        {
            return ProcessingResult.Invalid(error);
        }

        // 2. Same eventId already taken → don't count it again.
        if (!deduplication.TryBegin(telemetryEvent.EventId))
        {
            return ProcessingResult.Duplicate;
        }

        try
        {
            // 3. Save. Retries, backoff and circuit breaker live inside the persistence decorator.
            await persistence.SaveAsync(telemetryEvent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Retries exhausted (or circuit open): release the eventId so a reprocess from the DLQ can count it.
            deduplication.Release(telemetryEvent.EventId);

            logger.LogError(ex, "Giving up on {EventId}", telemetryEvent.EventId);

            return ProcessingResult.Failed($"persistence failed: {ex.Message}");
        }

        // 4. Count it only once it is safely stored → counted exactly once.
        deviceStats.RecordValidEvent(telemetryEvent.DeviceId, telemetryEvent.Timestamp);

        logger.LogDebug("Processed {DeviceId} {EventId}", telemetryEvent.DeviceId, telemetryEvent.EventId);

        return ProcessingResult.Ok;
    }
}