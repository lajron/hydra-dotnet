namespace Hydra.Infrastructure.Configuration;

public static class InfrastructureConfiguration
{
    [RequiresUnreferencedCode("Reflection-based operation discovery requires operation types and interfaces to be preserved when trimming.")]
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddOptionsFromAssembly(AssemblyReference.Assembly);
        services.AddPersistenceConfiguration();
        services.AddOperationServices(AssemblyReference.Assembly);
        return services;
    }
}
