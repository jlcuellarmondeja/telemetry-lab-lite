using System.Collections.Concurrent;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Processing;
using Telemetry.Domain.Events;

namespace Telemetry.UnitTests.Utils;

// Writes down, per device, the order in which events reached the processor.
internal sealed class RecordingProcessor : ITelemetryProcessor
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Guid>> _byDevice = new();

    public async Task<ProcessingResult> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        await Task.Yield(); // let the partitions interleave, as in real life

        _byDevice.GetOrAdd(telemetryEvent.DeviceId, _ => new())
                 .Enqueue(telemetryEvent.EventId);

        return ProcessingResult.Ok;
    }

    public IEnumerable<Guid> ProcessedFor(string deviceId) => _byDevice[deviceId];
}
