using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;

namespace Telemetry.UnitTests.Utils;

// Persistence that fails only when we tell it to.
internal sealed class FakePersistence : ITelemetryPersistence
{
    public bool FailNext { get; set; }

    public Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("database down");
        }

        return Task.CompletedTask;
    }
}
