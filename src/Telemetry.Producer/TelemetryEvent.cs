namespace Telemetry.Contracts;

/// <summary>
/// The telemetry event contract. The Producer emits these as JSON.
/// Note: some emitted messages are intentionally malformed (poison)
/// and some are intentional duplicates (same EventId).
/// </summary>
public sealed record TelemetryEvent
{
    public Guid EventId { get; init; }
    public string DeviceId { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public double Lat { get; init; }
    public double Lng { get; init; }
    public double SpeedKph { get; init; }
    public double FuelLevel { get; init; }
    public bool EngineOn { get; init; }

    public const string QueueName = "telemetry.events";
}
