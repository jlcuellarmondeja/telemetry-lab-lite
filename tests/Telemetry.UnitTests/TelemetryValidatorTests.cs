using Telemetry.Domain.Events;
using Telemetry.Domain.Validation;
using Telemetry.UnitTests.Utils;

namespace Telemetry.UnitTests;

// Requirement 3 (Security) — input validation: reject out-of-range events
// (negative fuel, impossible speed, future timestamps). Uses the producer's own bad values.
[TestFixture]
public class TelemetryValidatorTests
{
    [Test]
    public void ValidEvent_HasNoError()
        => Assert.That(TelemetryValidator.Validate(EventTestData.Valid(), EventTestData.Now), Is.Null);

    [Test]
    public void Speed9999_IsInvalid()
        => AssertInvalid(EventTestData.Valid() with { SpeedKph = 9999 }, "speedKph");

    [Test]
    public void NegativeFuel_IsInvalid()
        => AssertInvalid(EventTestData.Valid() with { FuelLevel = -1 }, "fuelLevel");

    [Test]
    public void TimestampFiveYearsAhead_IsInvalid()
        => AssertInvalid(EventTestData.Valid() with { Timestamp = EventTestData.Now.AddYears(5) }, "future");

    // Device clocks drift a little: a small skew must NOT be rejected.
    [Test]
    public void SmallClockSkew_IsAccepted()
        => Assert.That(TelemetryValidator.Validate(EventTestData.Valid() with { Timestamp = EventTestData.Now.AddMinutes(2) }, EventTestData.Now), Is.Null);

    [Test]
    public void EmptyDeviceId_IsInvalid()
        => AssertInvalid(EventTestData.Valid() with { DeviceId = "" }, "deviceId");

    // Does.Contain also fails when the result is null (= the event was considered valid).
    private static void AssertInvalid(TelemetryEvent e, string expectedInReason)
        => Assert.That(TelemetryValidator.Validate(e, EventTestData.Now), Does.Contain(expectedInReason));
}
