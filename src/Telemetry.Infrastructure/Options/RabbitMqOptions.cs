using System.ComponentModel.DataAnnotations;

namespace Telemetry.Infrastructure.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string? ClientName { get; init; }

    [Required]
    public string Host { get; init; } = default!;

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    [Required]
    public string UserName { get; init; } = default!;

    [Required]
    public string Password { get; init; } = default!;
    public string VirtualHost { get; init; } = "/";

    // Maximum number of unacknowledged messages that Rabbit delivers to us at once.
    [Range(1, ushort.MaxValue)]
    public ushort PrefetchCount { get; init; } = 100;

    [Required]
    public string QueueName { get; init; } = default!;

    [Required]
    public string QuarantineQueueName { get; init; } = default!;

    [Required]
    public string DeadLetterQueueName { get; init; } = default!;
}
