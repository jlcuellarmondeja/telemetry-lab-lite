using Telemetry.Infrastructure.Messaging.RabbitMq.Consumers;

namespace Telemetry.Worker.Workers;

public sealed class TelemetryConsumerWorker(RabbitMqConsumer consumer)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // do not block host startup (or /health)
        consumer.Start();
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Stop(); // stops receiving
        }
    }
}
