using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;
using Telemetry.Infrastructure.Persistence;

namespace Telemetry.UnitTests.Utils;

// Fails the first N calls with a transient error, then works. int.MaxValue = always down.
internal sealed class FailingPersistence(int failures) : ITelemetryPersistence
{
    private int _calls;
    public int Calls => _calls;

    public Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call <= failures)
            throw new TransientPersistenceException($"failure #{call}");

        return Task.CompletedTask;
    }
}
