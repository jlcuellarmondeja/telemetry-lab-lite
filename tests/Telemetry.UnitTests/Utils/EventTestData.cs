using Telemetry.Domain.Events;

namespace Telemetry.UnitTests.Utils;

internal static class EventTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // A valid event; each test changes only the field it cares about with "with { ... }".
    public static TelemetryEvent Valid(string deviceId = "truck-1") => new()
    {
        EventId = Guid.NewGuid(),
        DeviceId = deviceId,
        Timestamp = Now,
        Lat = 40.4,
        Lng = -3.7,
        SpeedKph = 80,
        FuelLevel = 0.5,
        EngineOn = true
    };
}
