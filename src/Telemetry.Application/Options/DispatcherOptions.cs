namespace Telemetry.Application.Options;

public sealed class DispatcherOptions
{
    public const string SectionName = "Dispatcher";

    // Number of rows = maximum number of events being processed at the same time.
    public int Partitions { get; init; } = 8;

    // How many events can fit waiting in each row before stopping the consumer.
    public int PartitionCapacity { get; init; } = 50;
}
