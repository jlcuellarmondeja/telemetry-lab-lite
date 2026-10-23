using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Application.Abstractions;
using Telemetry.Infrastructure.Deduplication;
using Telemetry.Infrastructure.Options;

namespace Telemetry.Infrastructure.DI;

public static class DeduplicationDependencyInjection
{
    public static IServiceCollection AddInMemoryDeduplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DeduplicationOptions>()
            .Bind(configuration.GetSection(DeduplicationOptions.SectionName))
            .Validate(o => o.Ttl > TimeSpan.Zero, "Deduplication: Ttl must be greater than 0")
            .ValidateOnStart();

        services.AddSingleton<IDeduplicationStore, InMemoryDeduplicationStore>();
        return services;
    }
}
