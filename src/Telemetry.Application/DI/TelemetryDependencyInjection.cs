using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Application.Abstractions;
using Telemetry.Application.Dispatching;
using Telemetry.Application.Options;
using Telemetry.Application.Processing;
using Telemetry.Application.Stats;

namespace Telemetry.Infrastructure.DI;

public static class TelemetryDependencyInjection
{
    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DispatcherOptions>()
            .Bind(configuration.GetSection(DispatcherOptions.SectionName))
            .Validate(o => o.Partitions > 0 && o.PartitionCapacity > 0,
                "Dispatcher: Partitions and PartitionCapacity must be greater than 0")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PipelineStats>();

        services.AddSingleton<ITelemetryProcessor, TelemetryProcessor>();

        services.AddSingleton<PartitionedDispatcher>();
        return services;
    }
}
