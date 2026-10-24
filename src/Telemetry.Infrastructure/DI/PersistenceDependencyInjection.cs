using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Application.Abstractions;
using Telemetry.Infrastructure.Options;
using Telemetry.Infrastructure.Persistence;

namespace Telemetry.Infrastructure.DI;

public static class PersistenceDependencyInjection
{
    public static IServiceCollection AddResilientPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName))
            .Validate(o => o.FaultInjectionRate is >= 0 and <= 1, "Persistence: FaultInjectionRate must be in [0, 1]")
            .Validate(o => o.MaxRetryAttempts > 0, "Persistence: MaxRetryAttempts must be greater than 0")
            .ValidateOnStart();

        // The real persistence...
        services.AddSingleton<InMemoryTelemetryPersistence>();

        // ...wrapped by the resilient decorator. Whoever asks for ITelemetryPersistence gets the decorator.
        services.AddSingleton<ITelemetryPersistence>(sp =>
            ActivatorUtilities.CreateInstance<ResilientTelemetryPersistence>(
                sp, sp.GetRequiredService<InMemoryTelemetryPersistence>()));

        return services;
    }
}
