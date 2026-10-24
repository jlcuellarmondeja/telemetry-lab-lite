namespace Telemetry.Application.Options;

public sealed class DispatcherOptions
{
    public const string SectionName = "Dispatcher";

    // Number of partitions = maximum number of events being processed at the same time.
    public int Partitions { get; init; } = 8;

    // How many events can wait in each partition before the consumer is paused (back-pressure).
    public int PartitionCapacity { get; init; } = 50;
}
