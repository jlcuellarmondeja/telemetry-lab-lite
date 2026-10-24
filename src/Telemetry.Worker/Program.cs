using Telemetry.Application.Abstractions;
using Telemetry.Application.DI;
using Telemetry.Application.Stats;
using Telemetry.Infrastructure.DI;
using Telemetry.Worker.Dtos;
using Telemetry.Worker.Workers;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTelemetry(builder.Configuration);
        builder.Services.AddRabbitMq(builder.Configuration);
        builder.Services.AddInMemoryDeduplication(builder.Configuration);
        builder.Services.AddInMemoryStats();
        builder.Services.AddResilientPersistence(builder.Configuration);

        builder.Services.AddHostedService<TelemetryConsumerWorker>();

        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/stats", (PipelineStats pipeline, IDeviceStatsStore devices) =>
        {
            var perDevice = devices.Snapshot();
            return Results.Ok(new StatsResponse(
                pipeline.Snapshot(),
                perDevice.Count,
                perDevice.OrderBy(d => d.Key).ToDictionary(d => d.Key, d => d.Value)));
        });

        app.Run();
    }
}