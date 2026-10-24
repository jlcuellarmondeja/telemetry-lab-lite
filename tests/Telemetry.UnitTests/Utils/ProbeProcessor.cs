using Telemetry.Application.Abstractions;
using Telemetry.Application.Processing;
using Telemetry.Domain.Events;

namespace Telemetry.UnitTests.Utils;

// Measures how many events are being processed AT THE SAME TIME.
// Device "poison" → Invalid; device "bomb" → throws (simulates an unexpected bug).
internal sealed class ProbeProcessor : ITelemetryProcessor
{
    private readonly object _lock = new();
    private int _current;
    public int MaxConcurrency { get; private set; }

    public async Task<ProcessingResult> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        if (telemetryEvent.DeviceId == "bomb") throw new InvalidOperationException("boom");
        if (telemetryEvent.DeviceId == "poison") return ProcessingResult.Invalid("bad data");

        lock (_lock) { _current++; MaxConcurrency = Math.Max(MaxConcurrency, _current); }
        await Task.Delay(20); // keep the partition reader busy so overlaps become visible
        lock (_lock) { _current--; }

        return ProcessingResult.Ok;
    }
}
