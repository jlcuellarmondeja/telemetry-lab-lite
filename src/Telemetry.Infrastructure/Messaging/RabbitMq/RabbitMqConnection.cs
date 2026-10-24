using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Reflection;
using Telemetry.Infrastructure.Options;

namespace Telemetry.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqConnection : IDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly object _lock = new();
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options)
    {
        var o = options.Value;

        var clientName = string.IsNullOrWhiteSpace(o.ClientName)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown"
            : o.ClientName;

        _factory = new ConnectionFactory
        {
            HostName = o.Host,
            Port = o.Port,
            UserName = o.UserName,
            Password = o.Password,
            VirtualHost = o.VirtualHost,
            DispatchConsumersAsync = true,   // required for AsyncEventingBasicConsumer
            AutomaticRecoveryEnabled = true, // reconnect if the broker drops AFTER we connected
            ClientProvidedName = $"{clientName}@{Environment.MachineName}"
        };
    }

    // Not Lazy<T>: Lazy caches the exception, so a failed first connect would fail forever.
    // Here a failure leaves _connection null and the next call simply tries again.
    public IModel CreateChannel()
    {
        lock (_lock)
        {
            _connection ??= _factory.CreateConnection();
            return _connection.CreateModel();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection?.Dispose();
        }
    }
}
