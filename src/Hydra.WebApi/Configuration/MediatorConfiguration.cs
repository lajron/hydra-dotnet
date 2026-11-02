using FluentValidation;
using Mediator;

namespace Hydra.WebApi.Configuration;

public static class MediatorConfiguration
{
    public static IServiceCollection AddMediatorServices(this IServiceCollection services)
    {
        services.AddMediator((MediatorOptions options) =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
        });

        services.AddValidatorsFromAssembly(Hydra.Application.AssemblyReference.Assembly, ServiceLifetime.Scoped);

        return services;
    }
}
