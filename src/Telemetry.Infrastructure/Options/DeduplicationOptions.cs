namespace Telemetry.Infrastructure.Options;

public sealed class DeduplicationOptions
{
    public const string SectionName = "Deduplication";

    // How long we remember an eventId. Longer = catches older duplicates, but uses more memory.
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(10);
}
