using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Infrastructure.Messaging.RabbitMq.Connections;
using Telemetry.Infrastructure.Messaging.RabbitMq.Consumers;
using Telemetry.Infrastructure.Options;

namespace Telemetry.Infrastructure.DI;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();   // fails to start, not at the first message

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqConsumer>();
        return services;
    }
}
