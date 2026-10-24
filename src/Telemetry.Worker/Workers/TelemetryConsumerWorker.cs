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

        dispatcher.Start(); // 1st: start the partition readers

        try
        {
            await consumer.StartAsync(stoppingToken);       // 2nd: start consuming (retries until the broker is up)
            await Task.Delay(Timeout.Infinite, stoppingToken); // work until SIGTERM / Ctrl+C
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown signal.
        }
        finally
        {
            logger.LogInformation("Shutdown requested: stopping consumption and draining in-flight work");
            var elapsed = Stopwatch.StartNew();

            consumer.Stop();                  // 1st: stop consuming → Rabbit sends nothing new
            await dispatcher.CompleteAsync(); // 2nd: partitions drain their in-flight work (and ack it)

            logger.LogInformation("All partitions drained in {Elapsed} ms", elapsed.ElapsedMilliseconds);
        }
    }
}