using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Telemetry.Application.Abstractions;
using Telemetry.Infrastructure.Options;

namespace Telemetry.Infrastructure.Deduplication;

// In-memory dedupe with a time window (TTL). Safe to call from all partitions at once.
public sealed class InMemoryDeduplicationStore(
    IOptions<DeduplicationOptions> options,
    TimeProvider timeProvider) : IDeduplicationStore
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seen = new();
    private readonly TimeSpan _ttl = options.Value.Ttl;
    private long _lastPurgeTicks;

    public bool TryBegin(Guid eventId)
    {
        var now = timeProvider.GetUtcNow();
        PurgeExpiredIfDue(now);

        // TryAdd is atomic: if two partitions race with the same eventId, only ONE gets true.
        return _seen.TryAdd(eventId, now);
    }

    public void Release(Guid eventId) => _seen.TryRemove(eventId, out _);

    // Forget eventIds older than the TTL, at most once per minute, so memory stays bounded.
    private void PurgeExpiredIfDue(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _lastPurgeTicks);
        if (now.UtcTicks - last < PurgeInterval.Ticks)
            return;

        // Only the thread that wins this swap purges; the others carry on.
        if (Interlocked.CompareExchange(ref _lastPurgeTicks, now.UtcTicks, last) != last)
            return;

        var cutoff = now - _ttl;
        foreach (var (eventId, seenAt) in _seen)
        {
            if (seenAt < cutoff)
                _seen.TryRemove(eventId, out _);
        }
    }
}
