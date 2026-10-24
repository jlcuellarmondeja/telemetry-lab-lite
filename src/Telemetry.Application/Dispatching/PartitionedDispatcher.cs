using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Options;
using Telemetry.Application.Processing;
using Telemetry.Application.Stats;

namespace Telemetry.Application.Dispatching;

// Distributes events across N partitions based on their deviceId.
// - Same deviceId → same partition → processed in order.
// - Different partitions → processed in parallel.
// - N fixed readers → bounded concurrency (never one Task per message).
public sealed class PartitionedDispatcher
{
    private readonly Channel<TelemetryEnvelope>[] _partitions;
    private readonly ITelemetryProcessor _processor;
    private readonly PipelineStats _stats;
    private readonly ILogger<PartitionedDispatcher> _logger;
    private Task[] _readers = [];

    public PartitionedDispatcher(
        IOptions<DispatcherOptions> options,
        ITelemetryProcessor processor,
        PipelineStats stats,
        ILogger<PartitionedDispatcher> logger)
    {
        _processor = processor;
        _stats = stats;
        _logger = logger;

        var o = options.Value;
        _partitions = new Channel<TelemetryEnvelope>[o.Partitions];

        for (var i = 0; i < o.Partitions; i++)
        {
            _partitions[i] = Channel.CreateBounded<TelemetryEnvelope>(new BoundedChannelOptions(o.PartitionCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // partition full → writer waits (back-pressure)
                SingleReader = true,                    // one reader per partition → order guaranteed
                SingleWriter = true                     // only the consumer writes
            });
        }
    }

    // Starts one long-lived reader per partition.
    public void Start()
    {
        _readers = _partitions
            .Select((partition, index) => Task.Run(() => ReadPartitionAsync(partition.Reader, index)))
            .ToArray();
    }

    // Writes the event to its device's partition. If the partition is full, waits.
    public ValueTask DispatchAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
        => _partitions[GetPartition(envelope.Event.DeviceId)].Writer.WriteAsync(envelope, cancellationToken);

    // Same text → same number for the lifetime of the process, which is all we need.
    // "& int.MaxValue" removes the sign so that the % operation never yields a negative result.
    public int GetPartition(string deviceId)
        => (StringComparer.Ordinal.GetHashCode(deviceId) & int.MaxValue) % _partitions.Length;

    // Completes every partition (no new writes) and waits for the readers to drain what is left.
    public async Task CompleteAsync()
    {
        foreach (var partition in _partitions)
        {
            partition.Writer.TryComplete();
        }

        await Task.WhenAll(_readers);
    }

    private async Task ReadPartitionAsync(ChannelReader<TelemetryEnvelope> reader, int partition)
    {
        // ReadAllAsync ends only when the partition is completed AND empty → natural draining.
        await foreach (var envelope in reader.ReadAllAsync())
        {
            try
            {
                var result = await _processor.ProcessAsync(envelope.Event, CancellationToken.None);

                switch (result.Outcome)
                {
                    case ProcessingOutcome.Invalid:

                        _stats.RecordQuarantined();

                        _logger.LogWarning("Quarantining {EventId}: {Reason}", envelope.Event.EventId, result.Reason);

                        envelope.Quarantine(result.Reason!);

                        break;

                    case ProcessingOutcome.Failed:

                        _stats.RecordDeadLettered();

                        _logger.LogError("Dead-lettering {EventId}: {Reason}", envelope.Event.EventId, result.Reason);

                        envelope.DeadLetter(result.Reason!);

                        break;

                    case ProcessingOutcome.Duplicate:

                        _stats.RecordDuplicate();

                        _logger.LogInformation("Duplicate {EventId} skipped", envelope.Event.EventId);

                        envelope.Ack();

                        break;

                    default:

                        _stats.RecordProcessed();

                        envelope.Ack();

                        break;
                }
            }
            catch (Exception ex)
            {
                // Unexpected bug: never kill the reader, never lose the message → DLQ.
                _stats.RecordDeadLettered();

                _logger.LogError(ex, "Partition {Partition} failed processing {EventId}", partition, envelope.Event.EventId);

                envelope.DeadLetter($"unexpected error: {ex.Message}");
            }
        }
    }
}
