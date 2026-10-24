using Telemetry.Domain.Events;

namespace Telemetry.Application.Dispatching;

// The event + how to settle it. Application doesn't know RabbitMQ is behind these callbacks.
public sealed record TelemetryEnvelope(
    TelemetryEvent Event,
    Action Ack,
    Action<string> Quarantine,
    Action<string> DeadLetter);