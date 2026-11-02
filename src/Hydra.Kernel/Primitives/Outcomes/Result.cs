namespace Hydra.Kernel.Primitives;

/// <summary>
/// Represents the result of an operation, which can either be a success or a failure with an associated application error.
/// </summary>
/// <remarks>
/// This class is immutable and provides factory methods for creating success or failure results.
/// </remarks>
public sealed class Result
{
    private AppError? Error { get; }

    private Result(AppError? error)
    {
        Error = error;
    }

    public static Result Success() => new(null);

    public static Result<T> Success<T>(T value) => Result<T>.CreateSuccess(value);

    public static implicit operator Result(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public void Deconstruct(out bool succeeded, out AppError? error)
    {
        succeeded = Error is null;
        error = Error;
    }
}

/// <summary>
/// Represents the result of an operation that can either be a success with a value of type T or a failure with an associated application error.
/// </summary>
/// <typeparam name="T">The type of the value returned on success.</typeparam>
/// <remarks>
/// This class is immutable and provides factory methods for creating success or failure results.
/// </remarks>
public sealed class Result<T>
{
    private T Value { get; }
    private AppError? Error { get; }
    
    private Result(T value)
    {
        Value = value;
        Error = null;
    }

    private Result(AppError error)
    {
        Value = default!;
        Error = error;
    }
    
    internal static Result<T> CreateSuccess(T value) => new(value);

    public static implicit operator Result<T>(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }
    
    public void Deconstruct(out T value, out AppError? error)
    {
        value = Value;
        error = Error;
    }   
}