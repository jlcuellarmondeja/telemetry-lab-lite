namespace Telemetry.Application.Stats;

// Immutable: every update creates a new instance, so readers never see a half-updated value.
public sealed record DeviceStats(long ValidEvents, DateTimeOffset LastEventAt);