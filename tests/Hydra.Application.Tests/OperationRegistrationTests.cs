using System.Reflection;
using System.Reflection.Emit;
using Hydra.Infrastructure.Configuration;
using Hydra.Kernel.Operations;
using Hydra.Kernel.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Hydra.Application.Tests;

#pragma warning disable RES001, RES002, RES003

public interface IFirstTestOperation : IAsyncOperation<int, int>;
public interface ISecondTestOperation : IAsyncOperation<int, int>;
public interface INoValueTestOperation : IAsyncOperation<int>;

internal sealed class FirstTestOperation : IFirstTestOperation
{
    public Guid InstanceId { get; } = Guid.NewGuid();
    public Task<Result<int>> ExecuteAsync(int input, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(input + 1));
}

internal sealed class SecondTestOperation : ISecondTestOperation
{
    public Task<Result<int>> ExecuteAsync(int input, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(input + 2));
}

internal sealed class NoValueTestOperation : INoValueTestOperation
{
    public CancellationToken SeenToken { get; private set; }
    public Task<Result> ExecuteAsync(int input, CancellationToken cancellationToken = default)
    {
        SeenToken = cancellationToken;
        return Task.FromResult(Result.Success());
    }
}

public class EmittedOperationBase : IAsyncOperation<int, int>
{
    public Task<Result<int>> ExecuteAsync(int input, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(input));
}

public sealed class OperationRegistrationTests
{
    [Fact]
    public async Task Named_operations_resolve_to_internal_implementations_with_scoped_lifetime()
    {
        using var provider = new ServiceCollection()
            .AddOperationServices(typeof(OperationRegistrationTests).Assembly)
            .BuildServiceProvider(validateScopes: true);

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IFirstTestOperation>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IFirstTestOperation>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<IFirstTestOperation>();
        var second = firstScope.ServiceProvider.GetRequiredService<ISecondTestOperation>();

        Assert.IsType<FirstTestOperation>(first);
        Assert.Same(first, sameScope);
        Assert.NotSame(first, otherScope);
        Assert.IsType<SecondTestOperation>(second);
        Assert.Equal(4, (await first.ExecuteAsync(3, TestContext.Current.CancellationToken)).DeconstructValue());
        Assert.Equal(5, (await second.ExecuteAsync(3, TestContext.Current.CancellationToken)).DeconstructValue());
        Assert.Empty(firstScope.ServiceProvider.GetServices<IAsyncOperation<int, int>>());
    }

    [Fact]
    public async Task Non_generic_result_operation_receives_cancellation_token()
    {
        using var provider = new ServiceCollection()
            .AddOperationServices(typeof(OperationRegistrationTests).Assembly)
            .BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        using var source = new CancellationTokenSource();
        var operation = Assert.IsType<NoValueTestOperation>(scope.ServiceProvider.GetRequiredService<INoValueTestOperation>());

        var (succeeded, error) = await operation.ExecuteAsync(3, source.Token);

        Assert.True(succeeded);
        Assert.Null(error);
        Assert.Equal(source.Token, operation.SeenToken);
    }

    [Fact]
    public void Multiple_implementations_fail_before_any_registration_is_added()
    {
        var assembly = CreateAmbiguousAssembly();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddOperationServices(assembly));

        Assert.Contains("IAmbiguousOperation", exception.Message);
        Assert.Contains("FirstImplementation", exception.Message);
        Assert.Contains("SecondImplementation", exception.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void Existing_named_registration_fails_clearly()
    {
        var services = new ServiceCollection().AddScoped<IFirstTestOperation, FirstTestOperation>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddOperationServices(typeof(OperationRegistrationTests).Assembly));

        Assert.Contains("IFirstTestOperation", exception.Message);
        Assert.Contains("already registered", exception.Message);
        Assert.Single(services);
    }

    private static Assembly CreateAmbiguousAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Hydra.AmbiguousOperations." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Operations");
        var namedInterface = module.DefineType("IAmbiguousOperation", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        namedInterface.AddInterfaceImplementation(typeof(IAsyncOperation<int, int>));
        var interfaceType = namedInterface.CreateType()!;

        foreach (var name in new[] { "FirstImplementation", "SecondImplementation" })
        {
            var implementation = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class, typeof(EmittedOperationBase));
            implementation.AddInterfaceImplementation(interfaceType);
            implementation.DefineDefaultConstructor(MethodAttributes.Public);
            implementation.CreateType();
        }

        return interfaceType.Assembly;
    }
}

internal static class ResultTestExtensions
{
    public static T DeconstructValue<T>(this Result<T> result)
    {
        var (value, error) = result;
        Assert.Null(error);
        return value;
    }
}
