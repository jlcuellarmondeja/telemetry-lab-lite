namespace Telemetry.Infrastructure.Options;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    // Retry: base delay doubles each attempt (200ms, 400ms, 800ms, 1.6s) plus jitter.
    public int MaxRetryAttempts { get; init; } = 4;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    // Circuit breaker: if >= 50% of at least 20 calls fail within 10s, stop calling for 15s.
    public double BreakerFailureRatio { get; init; } = 0.5;
    public int BreakerMinimumThroughput { get; init; } = 20;
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(15);

    // Chaos: fraction of calls that fail on purpose (0 = off). The brief's scenario is 0.2.
    public double FaultInjectionRate { get; init; } = 0;
}
