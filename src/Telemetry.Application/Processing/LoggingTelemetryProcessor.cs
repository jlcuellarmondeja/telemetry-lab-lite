using Microsoft.Extensions.Logging;
using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;
using Telemetry.Domain.Validation;

namespace Telemetry.Application.Processing;

public sealed class LoggingTelemetryProcessor(TimeProvider timeProvider, ILogger<LoggingTelemetryProcessor> logger) : ITelemetryProcessor
{
    public Task<ProcessingResult> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        // TimeProvider instead of DateTimeOffset.UtcNow → tests can fix "now".
        var error = TelemetryValidator.Validate(telemetryEvent, timeProvider.GetUtcNow());
        if (error is not null)
            return Task.FromResult(ProcessingResult.Invalid(error));

        logger.LogDebug("Processed {DeviceId} {EventId}", telemetryEvent.DeviceId, telemetryEvent.EventId);
        return Task.FromResult(ProcessingResult.Ok);
    }
}
