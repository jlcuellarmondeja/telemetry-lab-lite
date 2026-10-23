using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Options;
using Telemetry.Application.Processing;

namespace Telemetry.Application.Dispatching;

// Distribute events across N rows based on their deviceId.
// - Same deviceId → same row → processed in order.
// - Different rows → processed in parallel.
// - N fixed readers → bounded concurrency (never one Task per message).
public sealed class PartitionedDispatcher
{
    private readonly Channel<TelemetryEnvelope>[] _partitions;
    private readonly ITelemetryProcessor _processor;
    private readonly ILogger<PartitionedDispatcher> _logger;
    private Task[] _readers = [];

    public PartitionedDispatcher(
        IOptions<DispatcherOptions> options,
        ITelemetryProcessor processor,
        ILogger<PartitionedDispatcher> logger)
    {
        _processor = processor;
        _logger = logger;

        var o = options.Value;
        _partitions = new Channel<TelemetryEnvelope>[o.Partitions];

        for (var i = 0; i < o.Partitions; i++)
        {
            _partitions[i] = Channel.CreateBounded<TelemetryEnvelope>(new BoundedChannelOptions(o.PartitionCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // queue full → writer waits (back-pressure)
                SingleReader = true,                    // a single processor for the queue → order guaranteed
                SingleWriter = true                     // only the consumer writes
            });
        }
    }

    // Open the boxes: one fixed reader per row.
    public void Start()
    {
        _readers = _partitions
            .Select((partition, index) => Task.Run(() => ReadPartitionAsync(partition.Reader, index)))
            .ToArray();
    }

    // Put the event in the row of its device. If the row is full, wait.
    public ValueTask DispatchAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
        => _partitions[GetPartition(envelope.Event.DeviceId)].Writer.WriteAsync(envelope, cancellationToken);

    // Same text → same number for the lifetime of the process, which is all we need.
    // "& int.MaxValue" removes the sign so that the % operation never yields a negative result.
    public int GetPartition(string deviceId)
        => (StringComparer.Ordinal.GetHashCode(deviceId) & int.MaxValue) % _partitions.Length;

    // Close the rows (no new items enter) and wait for the cashiers to drain what they have.
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
        // ReadAllAsync ends only when the row is completed AND empty → natural draining.
        await foreach (var envelope in reader.ReadAllAsync())
        {
            try
            {
                var result = await _processor.ProcessAsync(envelope.Event, CancellationToken.None);

                if (result.Outcome == ProcessingOutcome.Invalid)
                {
                    _logger.LogWarning("Quarantining {EventId}: {Reason}", envelope.Event.EventId, result.Reason);
                    envelope.Quarantine(result.Reason!);
                    continue;
                }

                if (result.Outcome == ProcessingOutcome.Duplicate)
                {
                    _logger.LogInformation("Duplicate {EventId} skipped", envelope.Event.EventId);
                }

                envelope.Ack();
            }
            catch (Exception ex)
            {
                // A failure NEVER kills the reader or blocks the row.
                _logger.LogError(ex, "Partition {Partition} failed processing {EventId}", partition, envelope.Event.EventId);
                envelope.Ack();
            }
        }
    }
}
