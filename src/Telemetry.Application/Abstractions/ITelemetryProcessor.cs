using Telemetry.Application.Processing;
using Telemetry.Domain.Events;

namespace Telemetry.Application.Abstractions;

public interface ITelemetryProcessor
{
    Task<ProcessingResult> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken);
}
