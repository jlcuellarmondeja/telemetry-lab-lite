using Microsoft.Extensions.DependencyInjection;
using Telemetry.Application.Abstractions;
using Telemetry.Infrastructure.Stats;

namespace Telemetry.Infrastructure.DI;

public static class StatsDependencyInjection
{
    public static IServiceCollection AddInMemoryStats(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceStatsStore, InMemoryDeviceStatsStore>();
        return services;
    }
}
