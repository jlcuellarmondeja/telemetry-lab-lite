using Telemetry.Domain.Events;

namespace Telemetry.Application.Abstractions;

public interface ITelemetryProcessor
{
    Task ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken);
}
