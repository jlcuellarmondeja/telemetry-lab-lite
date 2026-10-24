namespace Telemetry.Application.Stats;

// Pipeline totals. Interlocked = atomic increment: safe from every partition and the consumer at once.
public sealed class PipelineStats
{
    private long _received;
    private long _processed;
    private long _duplicates;
    private long _quarantined;
    private long _retries;
    private long _deadLettered;

    public void RecordReceived() => Interlocked.Increment(ref _received);
    public void RecordProcessed() => Interlocked.Increment(ref _processed);
    public void RecordDuplicate() => Interlocked.Increment(ref _duplicates);
    public void RecordQuarantined() => Interlocked.Increment(ref _quarantined);
    public void RecordRetry() => Interlocked.Increment(ref _retries);
    public void RecordDeadLettered() => Interlocked.Increment(ref _deadLettered);

    public PipelineStatsSnapshot Snapshot() => new(
        Interlocked.Read(ref _received),
        Interlocked.Read(ref _processed),
        Interlocked.Read(ref _duplicates),
        Interlocked.Read(ref _quarantined),
        Interlocked.Read(ref _retries),
        Interlocked.Read(ref _deadLettered));
}

public sealed record PipelineStatsSnapshot(
    long Received, long Processed, long Duplicates, long Quarantined, long Retries, long DeadLettered);