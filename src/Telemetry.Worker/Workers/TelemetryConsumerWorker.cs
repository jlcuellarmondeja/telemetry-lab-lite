using Telemetry.Application.Dispatching;
using Telemetry.Infrastructure.Messaging.RabbitMq.Consumers;

namespace Telemetry.Worker.Workers;

public sealed class TelemetryConsumerWorker(
    RabbitMqConsumer consumer,
    PartitionedDispatcher dispatcher,
    ILogger<TelemetryConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // do not block host startup (or /health)

        dispatcher.Start(); // 1st: open the boxes
        consumer.Start();   // 2nd: open the door

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Stop();                  // 1st: close the door: no new messages will be received
            await dispatcher.CompleteAsync(); // 2nd: attend to those already in the queues (and acknowledge them)
            logger.LogInformation("All partitions drained");
        }
    }
}
