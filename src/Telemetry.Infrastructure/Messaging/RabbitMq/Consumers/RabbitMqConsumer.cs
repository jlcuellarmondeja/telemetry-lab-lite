using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Telemetry.Application.Abstractions;
using Telemetry.Infrastructure.Messaging.RabbitMq.Connections;
using Telemetry.Infrastructure.Messaging.RabbitMq.Options;

namespace Telemetry.Infrastructure.Messaging.RabbitMq.Consumers;

public sealed class RabbitMqConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ITelemetryMessageHandler handler,
    ILogger<RabbitMqConsumer> logger) : IDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private IModel? _channel;
    private string? _consumerTag;

    public void Start()
    {
        _channel = connection.CreateChannel();

        // Same as for the producer; if it differs → PRECONDITION_FAILED.
        _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

        // THE LIMIT: Rabbit does not send us more than PrefetchCount messages without an acknowledgment.
        _channel.BasicQos(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnReceivedAsync;

        // autoAck:false → without this, BasicQos has no effect.
        _consumerTag = _channel.BasicConsume(_options.QueueName, autoAck: false, consumer);

        logger.LogInformation("Consuming {Queue} with prefetch {Prefetch}", _options.QueueName, _options.PrefetchCount);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            await handler.HandleAsync(ea.Body, CancellationToken.None);
            _channel!.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // Nunca requeue:true aquí → bucle infinito con poison messages.
            logger.LogError(ex, "Failed to handle delivery {Tag}", ea.DeliveryTag);
            _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public void Stop()
    {
        if (_consumerTag is not null && _channel is { IsOpen: true })
            _channel.BasicCancel(_consumerTag);
    }

    public void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
    }
}
