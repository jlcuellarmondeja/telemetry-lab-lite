using Microsoft.Extensions.Logging;
using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;

namespace Telemetry.Application.Processing;

public sealed class LoggingTelemetryProcessor(ILogger<LoggingTelemetryProcessor> logger) : ITelemetryProcessor
{
    public Task ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processed {DeviceId} {EventId} on thread {Thread}",
                               telemetryEvent.DeviceId,
                               telemetryEvent.EventId,
                               Environment.CurrentManagedThreadId);

        return Task.CompletedTask;
    }
}
