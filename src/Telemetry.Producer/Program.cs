using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Telemetry.Contracts;

// ---------------------------------------------------------------------------
// Telemetry event producer. PROVIDED — you do not need to modify this.
// Emits events to RabbitMQ at a configurable rate, with a configurable
// fraction of duplicates (same EventId resent) and poison messages
// (malformed JSON / out-of-range values) so you can exercise idempotency
// and poison-message handling in the Worker.
// ---------------------------------------------------------------------------

double rate = GetArg("--rate", 100);
int devices = (int)GetArg("--devices", 20);
double duplicateRate = GetArg("--duplicate-rate", 0.05);
double poisonRate = GetArg("--poison-rate", 0.02);

var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var user = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
var pass = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "guest";

var factory = new ConnectionFactory { HostName = host, UserName = user, Password = pass };
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();
channel.QueueDeclare(TelemetryEvent.QueueName, durable: true, exclusive: false, autoDelete: false);

var props = channel.CreateBasicProperties();
props.Persistent = true;

var rng = new Random();
var lastSpeed = new Dictionary<string, double>();
var recentIds = new Queue<Guid>();
var delayMs = (int)(1000.0 / Math.Max(rate, 1));

Console.WriteLine($"Producing ~{rate} ev/s across {devices} devices " +
                  $"(dupes={duplicateRate:P0}, poison={poisonRate:P0}). Ctrl+C to stop.");

long sent = 0;
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.IsCancellationRequested)
{
    byte[] body;

    if (rng.NextDouble() < poisonRate)
    {
        // Poison: either invalid JSON or a semantically impossible event.
        body = rng.Next(2) == 0
            ? Encoding.UTF8.GetBytes("{ this is not valid json ]]")
            : JsonSerializer.SerializeToUtf8Bytes(MakeEvent(rng, devices, lastSpeed, poisoned: true));
    }
    else if (recentIds.Count > 0 && rng.NextDouble() < duplicateRate)
    {
        // Duplicate: resend a recent event verbatim (same EventId).
        var dupId = recentIds.ElementAt(rng.Next(recentIds.Count));
        var ev = MakeEvent(rng, devices, lastSpeed, fixedId: dupId);
        body = JsonSerializer.SerializeToUtf8Bytes(ev);
    }
    else
    {
        var ev = MakeEvent(rng, devices, lastSpeed);
        recentIds.Enqueue(ev.EventId);
        if (recentIds.Count > 200) recentIds.Dequeue();
        body = JsonSerializer.SerializeToUtf8Bytes(ev);
    }

    channel.BasicPublish("", TelemetryEvent.QueueName, props, body);
    if (++sent % 500 == 0) Console.WriteLine($"  sent {sent}");
    try { await Task.Delay(delayMs, cts.Token); } catch (OperationCanceledException) { }
}

Console.WriteLine($"Stopped. Total sent: {sent}");

static TelemetryEvent MakeEvent(Random rng, int devices,
    Dictionary<string, double> lastSpeed, bool poisoned = false, Guid? fixedId = null)
{
    var deviceId = $"vehicle-{rng.Next(devices):D4}";
    var prev = lastSpeed.GetValueOrDefault(deviceId, 50);
    // Occasionally inject a harsh delta so candidates can detect it.
    var delta = rng.NextDouble() < 0.1 ? rng.Next(40, 90) : rng.Next(-10, 10);
    var speed = Math.Clamp(prev + delta, 0, 180);
    lastSpeed[deviceId] = speed;

    return new TelemetryEvent
    {
        EventId = fixedId ?? Guid.NewGuid(),
        DeviceId = deviceId,
        Timestamp = poisoned ? DateTimeOffset.UtcNow.AddYears(5) : DateTimeOffset.UtcNow,
        Lat = -0.18 + rng.NextDouble() * 0.01,
        Lng = -78.46 + rng.NextDouble() * 0.01,
        SpeedKph = poisoned ? 9999 : speed,
        FuelLevel = poisoned ? -1 : Math.Round(rng.NextDouble(), 2),
        EngineOn = true
    };
}

static double GetArg(string name, double fallback)
{
    var args = Environment.GetCommandLineArgs();
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var v) ? v : fallback;
}
