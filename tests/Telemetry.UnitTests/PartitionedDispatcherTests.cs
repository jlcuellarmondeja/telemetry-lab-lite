using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Dispatching;
using Telemetry.Application.Options;
using Telemetry.Application.Stats;
using Telemetry.Domain.Events;
using Telemetry.UnitTests.Utils;

namespace Telemetry.UnitTests;

// Requirement 1 (Concurrency & parallelism) and Requirement 2 (Fault tolerance),
// tested on the dispatcher without RabbitMQ: the envelope callbacks just count.
[TestFixture]
public class PartitionedDispatcherTests
{
    private const int Partitions = 4;

    private PipelineStats _stats = null!;
    private int _acks, _quarantined, _deadLettered;

    [SetUp]
    public void SetUp()
    {
        _stats = new PipelineStats();
        _acks = _quarantined = _deadLettered = 0;
    }

    // Req 1: events of the SAME device are processed in arrival order,
    // even while 4 partitions work at once.
    [Test]
    public async Task SameDevice_IsProcessedInArrivalOrder()
    {
        var processor = new RecordingProcessor();
        var sent = await RunAsync(processor, Enumerable.Range(0, 200).Select(i => EventTestData.Valid($"truck-{i % 5}")));

        foreach (var device in sent.GroupBy(e => e.DeviceId))
        {
            Assert.That(processor.ProcessedFor(device.Key), Is.EqualTo(device.Select(e => e.EventId)));
        }
    }

    // Req 1: DIFFERENT devices overlap (parallel) but never beyond N partitions
    // (bounded concurrency, no Task per message).
    [Test]
    public async Task DifferentDevices_RunInParallel_ButNeverMoreThanPartitions()
    {
        var processor = new ProbeProcessor();
        await RunAsync(processor, Enumerable.Range(0, 40).Select(i => EventTestData.Valid($"truck-{i % 20}")));

        Assert.That(processor.MaxConcurrency, Is.GreaterThan(1));
        Assert.That(processor.MaxConcurrency, Is.LessThanOrEqualTo(Partitions));
    }

    // Req 2: a bug while processing doesn't kill the worker or block the pipeline (→ DLQ),
    // and poison events go to quarantine instead of being requeued forever.
    [Test]
    public async Task FailingAndPoisonEvents_AreSettled_AndPipelineKeepsGoing()
    {
        var events = new[]
        {
            EventTestData.Valid("bomb"),
            EventTestData.Valid("poison"),
            EventTestData.Valid("truck-1"),
            EventTestData.Valid("truck-2"),
        };

        await RunAsync(new ProbeProcessor(), events);

        Assert.That(_deadLettered, Is.EqualTo(1)); // the bug → DLQ, worker still alive
        Assert.That(_quarantined, Is.EqualTo(1));  // poison → quarantine, never requeued
        Assert.That(_acks, Is.EqualTo(2));         // the healthy ones still got through
        Assert.That(_stats.Snapshot().DeadLettered, Is.EqualTo(1));
    }

    // Req 2 (graceful shutdown): completing the partitions drains all in-flight work, nothing is lost.
    [Test]
    public async Task Complete_DrainsEverything_AndAcksEveryMessage()
    {
        await RunAsync(new RecordingProcessor(), Enumerable.Range(0, 200).Select(i => EventTestData.Valid($"truck-{i % 5}")));

        Assert.That(_acks, Is.EqualTo(200));
    }

    // Start → send everything → complete the partitions and wait for them to drain (= what shutdown does).
    // PartitionCapacity 10 < 200 events, so back-pressure (writer waits) is exercised too.
    private async Task<List<TelemetryEvent>> RunAsync(ITelemetryProcessor processor, IEnumerable<TelemetryEvent> events)
    {
        var dispatcher = new PartitionedDispatcher(
            Options.Create(new DispatcherOptions { Partitions = Partitions, PartitionCapacity = 10 }),
            processor,
            _stats,
            NullLogger<PartitionedDispatcher>.Instance);

        dispatcher.Start();

        var sent = new List<TelemetryEvent>();
        foreach (var e in events)
        {
            sent.Add(e);
            await dispatcher.DispatchAsync(new TelemetryEnvelope(
                e,
                () => Interlocked.Increment(ref _acks),
                _ => Interlocked.Increment(ref _quarantined),
                _ => Interlocked.Increment(ref _deadLettered)), default);
        }

        await dispatcher.CompleteAsync();
        return sent;
    }
}
