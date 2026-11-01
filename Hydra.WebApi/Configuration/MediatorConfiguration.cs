using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Hydra.WebApi.Configuration;

public static class MediatorConfiguration
{
    public static IServiceCollection AddMediatorConfiguration(this IServiceCollection services)
    {
        services.AddMediator((MediatorOptions options) =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
        });

        return services;
    }
}
