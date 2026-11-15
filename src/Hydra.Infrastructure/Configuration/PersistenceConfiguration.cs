namespace Hydra.Infrastructure.Configuration;

public static class PersistenceConfiguration
{
    public static IServiceCollection AddPersistenceConfiguration(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.PostgreSql));

        return services;
    }
}

public sealed class ConnectionStringsOptions
{
    [Required]
    public string PostgreSql { get; set; } = string.Empty;
}
