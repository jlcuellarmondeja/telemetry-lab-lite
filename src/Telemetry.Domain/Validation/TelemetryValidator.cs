using Telemetry.Domain.Events;

namespace Telemetry.Domain.Validation;

// Business rules for a telemetry event. Pure logic: no I/O, easy to unit test.
public static class TelemetryValidator
{
    public const int MaxDeviceIdLength = 64;
    public const double MaxSpeedKph = 300;
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5); // device clocks drift a bit
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    // Returns null when valid, otherwise the reason it is invalid.
    public static string? Validate(TelemetryEvent e, DateTimeOffset now)
    {
        if (e.EventId == Guid.Empty) return "eventId is empty";
        if (string.IsNullOrWhiteSpace(e.DeviceId) || e.DeviceId.Length > MaxDeviceIdLength) return "deviceId is missing or too long";
        if (e.FuelLevel is < 0 or > 1) return $"fuelLevel {e.FuelLevel} out of range [0, 1]";
        if (e.SpeedKph is < 0 or > MaxSpeedKph) return $"speedKph {e.SpeedKph} out of range [0, {MaxSpeedKph}]";
        if (e.Lat is < -90 or > 90) return $"lat {e.Lat} out of range";
        if (e.Lng is < -180 or > 180) return $"lng {e.Lng} out of range";
        if (e.Timestamp > now + MaxClockSkew) return $"timestamp {e.Timestamp:O} is in the future";
        if (e.Timestamp < now - MaxAge) return $"timestamp {e.Timestamp:O} is too old";
        return null;
    }
}
