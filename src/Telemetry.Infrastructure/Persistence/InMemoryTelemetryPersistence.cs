using System.Collections.Concurrent;
using Telemetry.Application.Abstractions;
using Telemetry.Domain.Events;

namespace Telemetry.Infrastructure.Persistence;

// Stores the latest event per device. The brief says no database is required;
// a real one (Postgres, Cosmos...) would replace this class without touching Application.
public sealed class InMemoryTelemetryPersistence : ITelemetryPersistence
{
    private readonly ConcurrentDictionary<string, TelemetryEvent> _lastEventByDevice = new();

    public Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        // Same device → same partition → writes for a device arrive in order.
        _lastEventByDevice[telemetryEvent.DeviceId] = telemetryEvent;
        return Task.CompletedTask;
    }
}