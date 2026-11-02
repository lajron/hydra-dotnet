using FluentValidation;
using Hydra.Kernel.Validation;
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
            options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
        });

        services.AddValidatorsFromAssembly(Hydra.Application.AssemblyReference.Assembly, ServiceLifetime.Scoped);

        return services;
    }
}
