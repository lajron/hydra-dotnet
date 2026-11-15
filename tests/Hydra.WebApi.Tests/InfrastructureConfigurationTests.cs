namespace Hydra.WebApi.Tests;

public sealed class InfrastructureConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Rejects_missing_postgresql_connection_string(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructureServices();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public void Registers_postgresql_context_with_scoped_lifetime()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "configured-value"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        Assert.Same(services, services.AddInfrastructureServices());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Same(first, sameScope);
        Assert.NotSame(first, otherScope);
        Assert.Equal("configured-value", firstScope.ServiceProvider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.PostgreSql);
        Assert.Equal("configured-value", first.Database.GetConnectionString());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", first.Database.ProviderName);
    }
}
