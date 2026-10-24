namespace Telemetry.UnitTests.Utils;

// Test-only doubles. They live here, never in production code.

// Frozen clock: "now" is always the same, so time-based rules are deterministic.
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
