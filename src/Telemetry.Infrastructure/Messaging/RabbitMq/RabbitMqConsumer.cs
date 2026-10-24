using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Text.Json;
using System.Threading.Channels;
using Telemetry.Application.Dispatching;
using Telemetry.Application.Stats;
using Telemetry.Domain.Events;
using Telemetry.Infrastructure.Options;

namespace Telemetry.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    PartitionedDispatcher dispatcher,
    PipelineStats stats,
    ILogger<RabbitMqConsumer> logger) : IDisposable
{
    // Web = case-sensitive regarding names. The producer sends "EventId", the PDF sends "eventId".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _options = options.Value;

    // IModel is not thread-safe, and all partition readers ack concurrently — hence, a lock.
    private readonly object _channelLock = new();

    private IModel? _channel;
    private string? _consumerTag;

    private static readonly TimeSpan MaxConnectDelay = TimeSpan.FromSeconds(30);

    // The broker may not be ready yet (pod started before RabbitMQ): instead of crashing,
    // retry with exponential backoff + jitter. Gives up as soon as shutdown is requested.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                Start();
                return;
            }
            catch (BrokerUnreachableException ex)
            {
                var wait = delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)); // jitter
                
                logger.LogWarning("RabbitMQ unreachable ({Reason}), retrying in {Delay}",
                                  ex.InnerException?.Message ?? ex.Message, wait);

                await Task.Delay(wait, cancellationToken); // throws if we are shutting down → stop trying

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxConnectDelay.Ticks)); // 1s, 2s, 4s… max 30s
            }
        }
    }

    private void Start()
    {
        _channel = connection.CreateChannel();

        // Same as for the producer; if it differs → PRECONDITION_FAILED.
        _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

        // Quarantine queue: must exist before publishing, otherwise Rabbit silently drops the message.
        _channel.QueueDeclare(_options.QuarantineQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

        // Dead-letter queue: events that still fail after all retries.
        _channel.QueueDeclare(_options.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

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
        stats.RecordReceived();

        var deliveryTag = ea.DeliveryTag;

        // COPY: RabbitMQ reuses ea.Body's buffer once this method returns,
        // and the quarantine callback may run later from a partition reader.
        var body = ea.Body.ToArray();

        var (telemetryEvent, error) = TryDeserialize(body);
        if (telemetryEvent is null)
        {
            stats.RecordQuarantined();

            // Poison (broken JSON / no deviceId): we can't even pick a partition → quarantine right here.
            PublishAndAck(deliveryTag, body, _options.QuarantineQueueName, "x-quarantine-reason", error!);
            return;
        }

        try
        {
            // If the device's partition is full, this waits → we stop pulling from Rabbit (back-pressure).
            await dispatcher.DispatchAsync(
                new TelemetryEnvelope(
                    telemetryEvent,
                    Ack: () => Ack(deliveryTag),
                    Quarantine: reason => PublishAndAck(deliveryTag, body, _options.QuarantineQueueName, "x-quarantine-reason", reason),
                    DeadLetter: reason => PublishAndAck(deliveryTag, body, _options.DeadLetterQueueName, "x-dead-letter-reason", reason)),
                CancellationToken.None);
        }
        catch (ChannelClosedException)
        {
            // Shutting down and the partitions are already completed: no ack → Rabbit will redeliver it.
            logger.LogInformation("Shutting down, delivery {Tag} left for redelivery", deliveryTag);
        }
    }

    private static (TelemetryEvent? Event, string? Error) TryDeserialize(byte[] body)
    {
        try
        {
            var telemetryEvent = JsonSerializer.Deserialize<TelemetryEvent>(body, JsonOptions);
            if (telemetryEvent is null) return (null, "empty payload");
            if (string.IsNullOrWhiteSpace(telemetryEvent.DeviceId)) return (null, "deviceId is missing");
            return (telemetryEvent, null);
        }
        catch (JsonException ex)
        {
            return (null, $"invalid JSON: {ex.Message}");
        }
    }

    private void Ack(ulong deliveryTag)
    {
        lock (_channelLock)
        {
            if (_channel is { IsOpen: true })
            {
                _channel.BasicAck(deliveryTag, multiple: false);
            }
        }
    }

    // Publish a copy to another queue + ack the original, under the same lock (IModel is not thread-safe).
    private void PublishAndAck(ulong deliveryTag, byte[] body, string queue, string reasonHeader, string reason)
    {
        lock (_channelLock)
        {
            if (_channel is not { IsOpen: true }) return;

            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            props.Headers = new Dictionary<string, object> { [reasonHeader] = reason };

            _channel.BasicPublish(exchange: "", routingKey: queue, basicProperties: props, body: body);
            _channel.BasicAck(deliveryTag, multiple: false);
        }
        logger.LogWarning("Delivery {Tag} sent to {Queue}: {Reason}", deliveryTag, queue, reason);
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