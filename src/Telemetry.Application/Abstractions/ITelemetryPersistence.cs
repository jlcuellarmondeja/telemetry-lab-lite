using Telemetry.Domain.Events;

namespace Telemetry.Application.Abstractions;

// The dependency where processed events are stored. It may fail transiently.
public interface ITelemetryPersistence
{
    Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken);
}
