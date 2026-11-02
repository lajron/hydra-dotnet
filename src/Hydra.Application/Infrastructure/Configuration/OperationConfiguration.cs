namespace Hydra.Application.Infrastructure.Configuration;

public static class OperationConfiguration
{
    [RequiresUnreferencedCode("Reflection-based operation discovery requires operation types and interfaces to be preserved when trimming.")]
    public static IServiceCollection AddOperationServices(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var registrations = new List<(Type Service, Type Implementation)>();

        foreach (var implementation in assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var namedInterfaces = implementation.GetInterfaces()
                .Where(type => type.IsInterface && !type.IsGenericType && type.Assembly == assembly &&
                               type.GetInterfaces().Any(IsOperationContract))
                .Distinct()
                .ToArray();

            if (namedInterfaces.Length == 0)
            {
                continue;
            }

            if (namedInterfaces.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Operation implementation {implementation.FullName} has multiple named operation interfaces: " +
                    string.Join(", ", namedInterfaces.Select(type => type.FullName).Order(StringComparer.Ordinal)) + ".");
            }

            var service = namedInterfaces[0];
            var contracts = service.GetInterfaces().Where(IsOperationContract).ToArray();
            if (contracts.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Named operation interface {service.FullName} must inherit exactly one IAsyncOperation contract.");
            }

            registrations.Add((service, implementation));
        }

        foreach (var group in registrations.GroupBy(item => item.Service))
        {
            if (group.Count() > 1)
            {
                throw new InvalidOperationException(
                    $"Named operation interface {group.Key.FullName} has multiple implementations: " +
                    string.Join(", ", group.Select(item => item.Implementation.FullName).Order(StringComparer.Ordinal)) + ".");
            }

            if (services.Any(descriptor => descriptor.ServiceType == group.Key))
            {
                throw new InvalidOperationException(
                    $"Named operation interface {group.Key.FullName} is already registered. Remove the duplicate registration.");
            }
        }

        foreach (var (service, implementation) in registrations)
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }

    private static bool IsOperationContract(Type type)
    {
        return type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(IAsyncOperation<,>) ||
             type.GetGenericTypeDefinition() == typeof(IAsyncOperation<>));
    }
}
