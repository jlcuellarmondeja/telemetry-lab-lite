using Telemetry.Infrastructure.DI;
using Telemetry.Worker.Workers;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTelemetry(builder.Configuration);
        builder.Services.AddRabbitMq(builder.Configuration);
        builder.Services.AddInMemoryDeduplication(builder.Configuration);

        builder.Services.AddHostedService<TelemetryConsumerWorker>();

        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.Run();
    }
}