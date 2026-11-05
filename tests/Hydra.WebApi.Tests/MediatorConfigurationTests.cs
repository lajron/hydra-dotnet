using Mediator;

namespace Hydra.WebApi.Tests;

public sealed class MediatorConfigurationTests
{
    [Fact]
    public void Registers_mediator_with_scoped_lifetime()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddMediatorServices());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMediator));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Same(first, sameScope);
        Assert.NotSame(first, otherScope);
    }
}
