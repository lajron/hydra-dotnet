using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace Hydra.Kernel.Validation;

public enum ValidationErrorCode
{
    InvalidRequest,
    InvalidField,
}

public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(message, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next(message, cancellationToken);
        }

        if (!FailureResponse<TResponse>.CanReturnFailure)
        {
            throw new ValidationException(failures);
        }

        var fieldErrors = failures.Select(failure => FieldValidationError.Create(
            failure.PropertyName,
            ValidationErrorCode.InvalidField,
            failure.ErrorCode,
            failure.ErrorMessage));
        var error = AppError.ValidationWithFields(ValidationErrorCode.InvalidRequest, fieldErrors);
        return FailureResponse<TResponse>.Create(error);
    }
}

internal static class FailureResponse<TResponse>
{
    public static bool CanReturnFailure { get; } = typeof(TResponse) == typeof(Result) ||
        (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>));

    private static readonly Func<AppError, TResponse> Factory = CreateFactory();

    public static TResponse Create(AppError error) => Factory(error);

    private static Func<AppError, TResponse> CreateFactory()
    {
        var responseType = typeof(TResponse);
        if (!CanReturnFailure)
        {
            return _ => throw new InvalidOperationException(
                $"{responseType} cannot represent a Hydra validation failure.");
        }

        var error = Expression.Parameter(typeof(AppError), "error");
        var conversion = Expression.Convert(error, responseType);
        return Expression.Lambda<Func<AppError, TResponse>>(conversion, error).Compile();
    }
}
