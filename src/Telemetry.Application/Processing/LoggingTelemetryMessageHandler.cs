using Microsoft.Extensions.Logging;
using Telemetry.Application.Abstractions;

namespace Telemetry.Application.Processing;

public sealed class LoggingTelemetryMessageHandler(ILogger<LoggingTelemetryMessageHandler> logger)
    : ITelemetryMessageHandler
{
    public Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received message ({Bytes} bytes)", body.Length);
        return Task.CompletedTask;
    }
}
