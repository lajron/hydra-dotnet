namespace Hydra.Infrastructure.Configuration;

public static class InfrastructureConfiguration
{
    [RequiresUnreferencedCode("Reflection-based operation discovery requires operation types and interfaces to be preserved when trimming.")]
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddOperationServices(AssemblyReference.Assembly);
        return services;
    }
}
