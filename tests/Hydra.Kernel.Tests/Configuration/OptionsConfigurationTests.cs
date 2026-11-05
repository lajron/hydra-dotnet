namespace Hydra.Kernel.Tests.Configuration;

public sealed class OptionsConfigurationTests
{
    [Fact]
    public void Discovers_only_options_in_the_supplied_assembly_and_binds_inferred_sections()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Sender"] = "sender@example.invalid",
                ["Jwt:LifetimeMinutes"] = "15",
                ["Storage:Root"] = "data"
            })
            .Build());

        services.AddOptionsFromAssembly(typeof(OptionsConfiguration).Assembly);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<EmailOptions>));

        Assert.Same(services, services.AddOptionsFromAssembly(typeof(EmailOptions).Assembly));
        var registrations = services.Count(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<EmailOptions>));
        services.AddOptionsFromAssembly(typeof(EmailOptions).Assembly);
        Assert.Equal(registrations, services.Count(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<EmailOptions>)));

        using var provider = services.BuildServiceProvider();
        Assert.Equal("sender@example.invalid", provider.GetRequiredService<IOptions<EmailOptions>>().Value.Sender);
        Assert.Equal(15, provider.GetRequiredService<IOptions<JwtOptions>>().Value.LifetimeMinutes);
        Assert.Equal("data", provider.GetRequiredService<IOptions<StorageOptions>>().Value.Root);
    }

    [Fact]
    public void Validates_data_annotations_when_options_are_resolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOptionsFromAssembly(typeof(EmailOptions).Assembly);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EmailOptions>>().Value);
    }

    [Fact]
    public void Adds_convention_binding_when_options_already_have_other_configuration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Sender"] = "sender@example.invalid"
            })
            .Build());
        services.Configure<EmailOptions>(_ => { });

        services.AddOptionsFromAssembly(typeof(EmailOptions).Assembly);

        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<EmailOptions>)));
        using var provider = services.BuildServiceProvider();
        Assert.Equal("sender@example.invalid", provider.GetRequiredService<IOptions<EmailOptions>>().Value.Sender);
    }

    [Fact]
    public async Task Validates_discovered_options_when_host_starts()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["Jwt:LifetimeMinutes"] = "15";
        builder.Services.AddOptionsFromAssembly(typeof(EmailOptions).Assembly);

        using var host = builder.Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }
}

public sealed class EmailOptions
{
    [Required]
    public string? Sender { get; set; }
}

public sealed class JwtOptions
{
    [Range(1, 60)]
    public int LifetimeMinutes { get; set; }
}

public sealed class StorageOptions
{
    public string? Root { get; set; }
}
