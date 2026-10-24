using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Simmy;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Stats;
using Telemetry.Domain.Events;
using Telemetry.Infrastructure.Options;

namespace Telemetry.Infrastructure.Persistence;

// Decorator: same interface as the real persistence, but every call goes through
// retry → circuit breaker → (optional) chaos fault. Application doesn't know Polly exists.
public sealed class ResilientTelemetryPersistence : ITelemetryPersistence
{
    private readonly ITelemetryPersistence _inner;
    private readonly ResiliencePipeline _pipeline;

    public ResilientTelemetryPersistence(
        ITelemetryPersistence inner,
        IOptions<PersistenceOptions> options,
        PipelineStats stats,
        ILogger<ResilientTelemetryPersistence> logger)
    {
        _inner = inner;
        var o = options.Value;

        _pipeline = new ResiliencePipelineBuilder()
            // OUTER: retry. Also retries when the circuit is open (fails fast, then waits the backoff).
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<TransientPersistenceException>()
                    .Handle<BrokenCircuitException>(),
                MaxRetryAttempts = o.MaxRetryAttempts,
                Delay = o.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential, // 200ms, 400ms, 800ms, 1.6s...
                UseJitter = true,                           // randomize so partitions don't retry in sync
                OnRetry = args =>
                {
                    stats.RecordRetry();
                    logger.LogWarning("Persistence attempt {Attempt} failed, retrying in {Delay}",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                }
            })
            // MIDDLE: circuit breaker. Every single attempt counts towards opening it.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<TransientPersistenceException>(),
                FailureRatio = o.BreakerFailureRatio,
                MinimumThroughput = o.BreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = o.BreakDuration,
                OnOpened = args =>
                {
                    logger.LogError("Circuit OPEN for {Duration}: persistence looks down", args.BreakDuration);
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuit CLOSED: persistence recovered");
                    return default;
                }
            })
            // INNER: chaos. Throws a transient failure in FaultInjectionRate of the calls (0 = never).
            // Used to reproduce the brief's "fails ~20% of the time" dependency without fake classes.
            .AddChaosFault(o.FaultInjectionRate, () => new TransientPersistenceException("Injected transient fault"))
            .Build();
    }

    public async Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
        => await _pipeline.ExecuteAsync(
            ct => new ValueTask(_inner.SaveAsync(telemetryEvent, ct)),
            cancellationToken);
}
