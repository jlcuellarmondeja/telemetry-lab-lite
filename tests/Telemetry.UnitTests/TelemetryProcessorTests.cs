using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Telemetry.Application.Processing;
using Telemetry.Infrastructure.Stats;
using Telemetry.Infrastructure.Deduplication;
using Telemetry.Infrastructure.Options;
using Telemetry.UnitTests.Utils;

namespace Telemetry.UnitTests;

// Requirement 3 (Security) — idempotency: the same eventId delivered twice must not be counted twice.
// Uses the REAL dedupe store and stats store; only persistence is faked.
[TestFixture]
public class TelemetryProcessorTests
{
    private FakePersistence _persistence = null!;
    private InMemoryDeviceStatsStore _stats = null!;
    private TelemetryProcessor _processor = null!;

    // NUnit reuses the fixture instance → rebuild everything before EACH test.
    [SetUp]
    public void SetUp()
    {
        var time = new FixedTimeProvider(EventTestData.Now);
        var dedupe = new InMemoryDeduplicationStore(Options.Create(new DeduplicationOptions()), time);

        _persistence = new FakePersistence();
        _stats = new InMemoryDeviceStatsStore();
        _processor = new TelemetryProcessor(dedupe, _persistence, _stats, time, NullLogger<TelemetryProcessor>.Instance);
    }

    // RabbitMQ is at-least-once: a redelivery must be detected and not counted again.
    [Test]
    public async Task SameEventIdTwice_IsCountedOnce()
    {
        var e = EventTestData.Valid();

        var first = await _processor.ProcessAsync(e, default);
        var second = await _processor.ProcessAsync(e, default);

        Assert.That(first.Outcome, Is.EqualTo(ProcessingOutcome.Processed));
        Assert.That(second.Outcome, Is.EqualTo(ProcessingOutcome.Duplicate));
        Assert.That(_stats.Snapshot()["truck-1"].ValidEvents, Is.EqualTo(1));
    }

    // Requirement 1 too (no race conditions): producer duplicates can land in different
    // partitions and arrive at the SAME time → exactly one must win.
    [Test]
    public async Task SameEventIdConcurrently_OnlyOneIsProcessed()
    {
        var e = EventTestData.Valid();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => Task.Run(() => _processor.ProcessAsync(e, default))));

        Assert.That(results.Count(r => r.Outcome == ProcessingOutcome.Processed), Is.EqualTo(1));
        Assert.That(_stats.Snapshot()["truck-1"].ValidEvents, Is.EqualTo(1));
    }

    // Dedupe must not swallow an event whose save failed: it goes to the DLQ and,
    // if reprocessed later, it must still be counted (once).
    [Test]
    public async Task FailedSave_ReleasesEventId_SoARetryCountsIt()
    {
        var e = EventTestData.Valid();
        _persistence.FailNext = true;

        var failed = await _processor.ProcessAsync(e, default);
        var retried = await _processor.ProcessAsync(e, default);

        Assert.That(failed.Outcome, Is.EqualTo(ProcessingOutcome.Failed));
        Assert.That(retried.Outcome, Is.EqualTo(ProcessingOutcome.Processed));
        Assert.That(_stats.Snapshot()["truck-1"].ValidEvents, Is.EqualTo(1));
    }

    // Requirement 3 (validation): an invalid event never reaches the counter.
    [Test]
    public async Task InvalidEvent_IsNotCounted()
    {
        var result = await _processor.ProcessAsync(EventTestData.Valid() with { FuelLevel = -1 }, default);

        Assert.That(result.Outcome, Is.EqualTo(ProcessingOutcome.Invalid));
        Assert.That(_stats.Snapshot(), Is.Empty);
    }
}
