namespace Hydra.Kernel.Configuration;

public static class OptionsConfiguration
{
    private const string OptionsSuffix = "Options";

    [RequiresUnreferencedCode("Reflection-based options discovery requires options types to be preserved when trimming.")]
    public static IServiceCollection AddOptionsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var registerMethod = typeof(OptionsConfiguration).GetMethod(nameof(RegisterOptions), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var type in assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract &&
                     !type.ContainsGenericParameters && type.Name.Length > OptionsSuffix.Length &&
                     type.Name.EndsWith(OptionsSuffix, StringComparison.Ordinal) &&
                     type.GetConstructor(Type.EmptyTypes) is not null)
                 .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var sectionName = type.Name[..^OptionsSuffix.Length];
            registerMethod.MakeGenericMethod(type).Invoke(null, [services, sectionName]);
        }

        return services;
    }

    private static void RegisterOptions<TOptions>(IServiceCollection services, string sectionName) where TOptions : class
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker<TOptions>)))
        {
            return;
        }

        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<RegistrationMarker<TOptions>>();
    }

    private sealed class RegistrationMarker<TOptions> where TOptions : class;
}
