using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Polly.CircuitBreaker;
using Telemetry.Application.Stats;
using Telemetry.Infrastructure.Options;
using Telemetry.Infrastructure.Persistence;
using Telemetry.UnitTests.Utils;

namespace Telemetry.UnitTests;

// Requirement 4 (Retry policies): retry transient failures, stop after the max (→ DLQ),
// and a circuit breaker around the fragile dependency.
// Chaos is off and delays are zero: failures come only from the fake, so tests are deterministic and fast.
[TestFixture]
public class ResilientTelemetryPersistenceTests
{
    private PipelineStats _stats = null!;

    [SetUp]
    public void SetUp() => _stats = new PipelineStats();

    // A transient failure is retried and the event finally gets saved.
    [Test]
    public async Task TransientFailures_AreRetried_UntilSuccess()
    {
        var inner = new FailingPersistence(failures: 2);

        await Create(inner, maxRetries: 4).SaveAsync(EventTestData.Valid(), default);

        Assert.That(inner.Calls, Is.EqualTo(3)); // 2 failures + 1 success
        Assert.That(_stats.Snapshot().Retries, Is.EqualTo(2));
    }

    // No infinite retries: after the max it gives up (the processor then sends it to the DLQ).
    [Test]
    public void AfterMaxRetries_GivesUp()
    {
        var inner = new FailingPersistence(failures: int.MaxValue);

        Assert.ThrowsAsync<TransientPersistenceException>(
            () => Create(inner, maxRetries: 3).SaveAsync(EventTestData.Valid(), default));

        Assert.That(inner.Calls, Is.EqualTo(4)); // 1 attempt + 3 retries, then stop
    }

    // Once the dependency looks down, the breaker opens and stops hammering it:
    // the remaining retries fail fast without touching the dependency.
    [Test]
    public void CircuitBreaker_OpensAndStopsCallingTheDependency()
    {
        var inner = new FailingPersistence(failures: int.MaxValue);

        Assert.ThrowsAsync<BrokenCircuitException>(
            () => Create(inner, maxRetries: 5, breakerMinimumThroughput: 2).SaveAsync(EventTestData.Valid(), default));

        Assert.That(inner.Calls, Is.EqualTo(2)); // breaker opened after 2 failures
    }

    private ResilientTelemetryPersistence Create(FailingPersistence inner, int maxRetries, int breakerMinimumThroughput = 100)
        => new(
            inner,
            Options.Create(new PersistenceOptions
            {
                FaultInjectionRate = 0,         // no random chaos
                MaxRetryAttempts = maxRetries,
                RetryBaseDelay = TimeSpan.Zero, // don't actually wait in tests
                BreakerFailureRatio = 0.5,
                BreakerMinimumThroughput = breakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(30)
            }),
            _stats,
            NullLogger<ResilientTelemetryPersistence>.Instance);
}
