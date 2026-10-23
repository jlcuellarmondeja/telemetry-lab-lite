namespace Telemetry.Application.Abstractions;

public interface ITelemetryMessageHandler
{
    Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);
}
