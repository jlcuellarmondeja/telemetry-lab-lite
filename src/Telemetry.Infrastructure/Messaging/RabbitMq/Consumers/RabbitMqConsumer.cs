using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using System.Threading.Channels;
using Telemetry.Application.Dispatching;
using Telemetry.Domain.Events;
using Telemetry.Infrastructure.Messaging.RabbitMq.Connections;
using Telemetry.Infrastructure.Messaging.RabbitMq.Options;

namespace Telemetry.Infrastructure.Messaging.RabbitMq.Consumers;

public sealed class RabbitMqConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    PartitionedDispatcher dispatcher,
    ILogger<RabbitMqConsumer> logger) : IDisposable
{
    // Web = case-sensitive regarding names. The producer sends "EventId", the PDF sends "eventId".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _options = options.Value;

    // IModel is not thread-safe, and now 8 cashiers are sending ACKs simultaneously — hence, a lock.
    private readonly object _channelLock = new();

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
        var deliveryTag = ea.DeliveryTag;

        var telemetryEvent = TryDeserialize(ea.Body.Span);
        if (telemetryEvent is null || string.IsNullOrWhiteSpace(telemetryEvent.DeviceId))
        {
            // Poison: Malformed JSON or missing deviceId (we wouldn't know which row to send it to).
            // Never re-enqueued. Block ③: instead of just an 'ack', it will be sent to the quarantine queue.
            logger.LogWarning("Poison message discarded (delivery {Tag})", deliveryTag);
            Ack(deliveryTag);
            return;
        }

        try
        {
            // If the device queue is full, it waits → we do not request any more from Rabbit.
            await dispatcher.DispatchAsync(new TelemetryEnvelope(telemetryEvent, () => Ack(deliveryTag)), CancellationToken.None);
        }
        catch (ChannelClosedException)
        {
            // We are shutting down and the queues are already closed: we do NOT send an ack.
            // Rabbit will redeliver it to another replica (or to us once we’re back up).
            logger.LogInformation("Shutting down, delivery {Tag} left for redelivery", deliveryTag);
        }
    }

    private TelemetryEvent? TryDeserialize(ReadOnlySpan<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<TelemetryEvent>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON");
            return null;
        }
    }

    private void Ack(ulong deliveryTag)
    {
        lock (_channelLock)
        {
            if (_channel is { IsOpen: true })
                _channel.BasicAck(deliveryTag, multiple: false);
        }
    }

    public void Stop()
    {
        lock (_channelLock)
        {
            if (_consumerTag is not null && _channel is { IsOpen: true })
                _channel.BasicCancel(_consumerTag);
        }
    }

    public void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
    }
}