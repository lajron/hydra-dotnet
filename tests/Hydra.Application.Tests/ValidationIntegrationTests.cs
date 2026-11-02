using FluentValidation;
using Hydra.Kernel.Primitives;
using Hydra.Kernel.Validation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hydra.Application.Tests;

// Tests inspect both success and failure values directly; the application analyzer expects
// production code to propagate failures rather than assert on them.
#pragma warning disable RES001, RES002, RES003

public sealed record PlainRequest : IRequest<Result>;
public sealed record ValidatedRequest(string Name, int Age) : IRequest<Result<string>>;
public sealed record AsyncRequest(string Value) : IRequest<Result>;
public sealed record CascadeRequest(string Value) : IRequest<Result>;
public sealed record SequentialRequest : IRequest<Result>;
public sealed record NonResultRequest(string Value) : IRequest<string>;

public sealed class ExecutionState
{
    public int Calls { get; set; }
    public Guid ScopeId { get; } = Guid.NewGuid();
    public Guid? ValidatorScopeId { get; set; }
    public Guid? HandlerScopeId { get; set; }
    public CancellationToken SeenToken { get; set; }
    public int ActiveValidators { get; private set; }
    public int PeakActiveValidators { get; private set; }
    public int CompletedValidators { get; private set; }

    public async Task<bool> FailAfterAsyncWork(CancellationToken cancellationToken)
    {
        lock (this)
        {
            ActiveValidators++;
            PeakActiveValidators = Math.Max(PeakActiveValidators, ActiveValidators);
        }

        try
        {
            await Task.Delay(30, cancellationToken);
            return false;
        }
        finally
        {
            lock (this)
            {
                ActiveValidators--;
                CompletedValidators++;
            }
        }
    }
}

public sealed class PlainRequestHandler(ExecutionState state) : IRequestHandler<PlainRequest, Result>
{
    public ValueTask<Result> Handle(PlainRequest request, CancellationToken cancellationToken)
    {
        state.Calls++;
        return ValueTask.FromResult(Result.Success());
    }
}

public sealed class ValidatedRequestHandler(ExecutionState state) : IRequestHandler<ValidatedRequest, Result<string>>
{
    public ValueTask<Result<string>> Handle(ValidatedRequest request, CancellationToken cancellationToken)
    {
        state.Calls++;
        state.HandlerScopeId = state.ScopeId;
        return ValueTask.FromResult(Result.Success(request.Name));
    }
}

public sealed class AsyncRequestHandler(ExecutionState state) : IRequestHandler<AsyncRequest, Result>
{
    public ValueTask<Result> Handle(AsyncRequest request, CancellationToken cancellationToken)
    {
        state.Calls++;
        return ValueTask.FromResult(Result.Success());
    }
}

public sealed class CascadeRequestHandler(ExecutionState state) : IRequestHandler<CascadeRequest, Result>
{
    public ValueTask<Result> Handle(CascadeRequest request, CancellationToken cancellationToken)
    {
        state.Calls++;
        return ValueTask.FromResult(Result.Success());
    }
}

public sealed class SequentialRequestHandler(ExecutionState state) : IRequestHandler<SequentialRequest, Result>
{
    public ValueTask<Result> Handle(SequentialRequest request, CancellationToken cancellationToken)
    {
        state.Calls++;
        return ValueTask.FromResult(Result.Success());
    }
}

public sealed class NonResultRequestHandler(ExecutionState state) : IRequestHandler<NonResultRequest, string>
{
    public ValueTask<string> Handle(NonResultRequest request, CancellationToken cancellationToken)
    {
        state.Calls++;
        return ValueTask.FromResult(request.Value);
    }
}

public sealed class NameValidator : AbstractValidator<ValidatedRequest>
{
    public NameValidator(ExecutionState state)
    {
        RuleFor(x => x.Name).NotEmpty().WithErrorCode("NameRequired");
        RuleFor(x => x.Name).MinimumLength(3).WithErrorCode("NameShort");
        RuleFor(x => x.Name).Custom((_, _) => state.ValidatorScopeId = state.ScopeId);
    }
}

public sealed class AgeValidator : AbstractValidator<ValidatedRequest>
{
    public AgeValidator()
    {
        RuleFor(x => x.Age).GreaterThan(0).WithErrorCode("AgeInvalid");
    }
}

public sealed class AsyncRequestValidator : AbstractValidator<AsyncRequest>
{
    public AsyncRequestValidator(ExecutionState state)
    {
        RuleFor(x => x.Value).MustAsync(async (value, token) =>
        {
            state.SeenToken = token;
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return value == "valid";
        }).WithErrorCode("ValueInvalid");
    }
}

public sealed class StopCascadeValidator : AbstractValidator<CascadeRequest>
{
    public StopCascadeValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleFor(x => x.Value)
            .NotEmpty().WithErrorCode("Required")
            .MinimumLength(3).WithErrorCode("TooShort");
        RuleFor(x => x.Value).NotEqual("").WithErrorCode("LaterRule");
    }
}

public sealed class ContinueCascadeValidator : AbstractValidator<CascadeRequest>
{
    public ContinueCascadeValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Continue;
        ClassLevelCascadeMode = CascadeMode.Continue;
        RuleFor(x => x.Value)
            .NotEmpty().WithErrorCode("AlsoRequired")
            .MinimumLength(3).WithErrorCode("AlsoTooShort");
    }
}

public sealed class FirstSequentialValidator : AbstractValidator<SequentialRequest>
{
    public FirstSequentialValidator(ExecutionState state)
    {
        RuleFor(x => x).MustAsync((_, token) => state.FailAfterAsyncWork(token))
            .WithErrorCode("FirstFailure");
    }
}

public sealed class SecondSequentialValidator : AbstractValidator<SequentialRequest>
{
    public SecondSequentialValidator(ExecutionState state)
    {
        RuleFor(x => x).MustAsync((_, token) => state.FailAfterAsyncWork(token))
            .WithErrorCode("SecondFailure");
    }
}

public sealed class NonResultRequestValidator : AbstractValidator<NonResultRequest>
{
    public NonResultRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty().WithErrorCode("ValueRequired");
    }
}

public sealed class ValidationIntegrationTests
{
    private static ServiceProvider CreateProvider() => new ServiceCollection()
        .AddScoped<ExecutionState>()
        .AddValidatorsFromAssemblyContaining<NameValidator>(ServiceLifetime.Scoped)
        .AddMediator((MediatorOptions options) =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
        })
        .BuildServiceProvider(validateScopes: true);

    [Fact]
    public async Task Request_without_validators_executes_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var (succeeded, error) = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new PlainRequest(), TestContext.Current.CancellationToken);
        Assert.True(succeeded);
        Assert.Null(error);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }

    [Fact]
    public async Task Passing_validation_executes_handler_in_the_same_scope()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var (value, error) = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ValidatedRequest("Alice", 20), TestContext.Current.CancellationToken);
        var state = scope.ServiceProvider.GetRequiredService<ExecutionState>();
        Assert.Equal("Alice", value);
        Assert.Null(error);
        Assert.Equal(1, state.Calls);
        Assert.Equal(state.ScopeId, state.ValidatorScopeId);
        Assert.Equal(state.ScopeId, state.HandlerScopeId);
    }

    [Fact]
    public async Task Single_failure_returns_a_failed_generic_result_without_running_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var (_, error) = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ValidatedRequest("Alice", 0), TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(AppErrorCategory.Validation, error.Category);
        Assert.Equal(ValidationErrorCode.InvalidRequest, error.Code);
        var field = Assert.Single(error.FieldErrors);
        Assert.Equal("Age", field.Field);
        Assert.Equal(ValidationErrorCode.InvalidField, field.Code);
        Assert.Equal("AgeInvalid", field.Parameters[0]);
        Assert.IsType<string>(field.Parameters[1]);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }

    [Fact]
    public async Task Failures_from_multiple_rules_and_validators_are_aggregated()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var (_, error) = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ValidatedRequest("", 0), TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(3, error.FieldErrors.Count);
        Assert.Contains(error.FieldErrors, x => x.Field == "Name" && Equals(x.Parameters[0], "NameRequired"));
        Assert.Contains(error.FieldErrors, x => x.Field == "Name" && Equals(x.Parameters[0], "NameShort"));
        Assert.Contains(error.FieldErrors, x => x.Field == "Age" && Equals(x.Parameters[0], "AgeInvalid"));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }

    [Fact]
    public async Task Validator_cascade_modes_control_reported_failures_without_stopping_other_validators()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var (succeeded, error) = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new CascadeRequest(""), TestContext.Current.CancellationToken);

        Assert.False(succeeded);
        Assert.NotNull(error);
        Assert.Equal(3, error.FieldErrors.Count);
        Assert.Contains(error.FieldErrors, x => Equals(x.Parameters[0], "Required"));
        Assert.Contains(error.FieldErrors, x => Equals(x.Parameters[0], "AlsoRequired"));
        Assert.Contains(error.FieldErrors, x => Equals(x.Parameters[0], "AlsoTooShort"));
        Assert.DoesNotContain(error.FieldErrors, x => Equals(x.Parameters[0], "TooShort"));
        Assert.DoesNotContain(error.FieldErrors, x => Equals(x.Parameters[0], "LaterRule"));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }

    [Fact]
    public async Task Async_validators_run_sequentially_and_aggregate_both_failures()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var (succeeded, error) = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new SequentialRequest(), TestContext.Current.CancellationToken);

        Assert.False(succeeded);
        Assert.NotNull(error);
        Assert.Equal(2, error.FieldErrors.Count);
        Assert.Contains(error.FieldErrors, x => Equals(x.Parameters[0], "FirstFailure"));
        Assert.Contains(error.FieldErrors, x => Equals(x.Parameters[0], "SecondFailure"));
        var state = scope.ServiceProvider.GetRequiredService<ExecutionState>();
        Assert.Equal(2, state.CompletedValidators);
        Assert.Equal(1, state.PeakActiveValidators);
        Assert.Equal(0, state.Calls);
    }

    [Fact]
    public async Task Non_result_request_uses_fluentvalidation_exception_on_failure()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var exception = await Assert.ThrowsAsync<ValidationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new NonResultRequest(""), TestContext.Current.CancellationToken));

        var failure = Assert.Single(exception.Errors);
        Assert.Equal("Value", failure.PropertyName);
        Assert.Equal("ValueRequired", failure.ErrorCode);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }

    [Fact]
    public async Task Passing_non_result_request_executes_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var response = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new NonResultRequest("valid"), TestContext.Current.CancellationToken);

        Assert.Equal("valid", response);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }

    [Fact]
    public async Task Async_validator_returns_failed_non_generic_result_and_receives_token()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var source = new CancellationTokenSource();
        var (succeeded, error) = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AsyncRequest("bad"), source.Token);
        Assert.False(succeeded);
        Assert.NotNull(error);
        Assert.Equal("Value", Assert.Single(error.FieldErrors).Field);
        var state = scope.ServiceProvider.GetRequiredService<ExecutionState>();
        Assert.Equal(source.Token, state.SeenToken);
        Assert.Equal(0, state.Calls);
    }

    [Fact]
    public async Task Cancellation_reaches_async_validator_and_stops_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AsyncRequest("valid"), source.Token));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionState>().Calls);
    }
}
