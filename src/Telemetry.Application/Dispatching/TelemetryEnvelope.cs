using Telemetry.Domain.Events;

namespace Telemetry.Application.Dispatching;

public sealed record TelemetryEnvelope(TelemetryEvent Event, Action Ack);