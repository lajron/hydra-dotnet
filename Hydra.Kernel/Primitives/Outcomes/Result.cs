namespace Hydra.Kernel.Primitives;

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