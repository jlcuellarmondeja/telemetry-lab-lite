using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Reflection;
using Telemetry.Infrastructure.Messaging.RabbitMq.Options;

namespace Telemetry.Infrastructure.Messaging.RabbitMq.Connections;

public sealed class RabbitMqConnection : IDisposable
{
    private readonly Lazy<IConnection> _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options)
    {
        var o = options.Value;

        var clientName = string.IsNullOrWhiteSpace(o.ClientName)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown"
            : o.ClientName;

        var factory = new ConnectionFactory
        {
            HostName = o.Host,
            Port = o.Port,
            UserName = o.UserName,
            Password = o.Password,
            VirtualHost = o.VirtualHost,
            DispatchConsumersAsync = true,   // required for AsyncEventingBasicConsumer
            AutomaticRecoveryEnabled = true, // reconnect only if the broker goes down
            ClientProvidedName = $"{clientName}@{Environment.MachineName}"
        };
        _connection = new Lazy<IConnection>(() => factory.CreateConnection());
    }

    public IModel CreateChannel() => _connection.Value.CreateModel();

    public void Dispose()
    {
        if (_connection.IsValueCreated) _connection.Value.Dispose();
    }
}
