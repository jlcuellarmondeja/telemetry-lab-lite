using Microsoft.Extensions.DependencyInjection;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Processing;

namespace Telemetry.Infrastructure.DI;

public static class TelemetryDependencyInjection
{
    public static IServiceCollection AddTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetryMessageHandler, LoggingTelemetryMessageHandler>();

        return services;
    }
}
