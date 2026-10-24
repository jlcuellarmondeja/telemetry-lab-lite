using System.Diagnostics;
using Telemetry.Application.Dispatching;
using Telemetry.Infrastructure.Messaging.RabbitMq;

namespace Telemetry.Worker.Workers;

public sealed class TelemetryConsumerWorker(
    RabbitMqConsumer consumer,
    PartitionedDispatcher dispatcher,
    ILogger<TelemetryConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // do not block host startup (or /health)

        dispatcher.Start(); // 1st: open the cashiers

        try
        {
            await consumer.StartAsync(stoppingToken);       // 2nd: open the door (retries until the broker is up)
            await Task.Delay(Timeout.Infinite, stoppingToken); // work until SIGTERM / Ctrl+C
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown signal.
        }
        finally
        {
            logger.LogInformation("Shutdown requested: closing the door and draining in-flight work");
            var elapsed = Stopwatch.StartNew();

            consumer.Stop();                  // 1st: close the door → Rabbit sends nothing new
            await dispatcher.CompleteAsync(); // 2nd: cashiers finish their rows (and ack them)

            logger.LogInformation("All partitions drained in {Elapsed} ms", elapsed.ElapsedMilliseconds);
        }
    }
}