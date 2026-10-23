namespace Telemetry.Application.Abstractions;

// Remembers which eventIds were already taken, so the same event is never counted twice.
public interface IDeduplicationStore
{
    // Atomically checks AND reserves the eventId in one step.
    // false → another delivery already has it (duplicate).
    bool TryBegin(Guid eventId);

    // Processing failed: forget the eventId so a retry/redelivery can process it.
    void Release(Guid eventId);
}
