namespace Hydra.Kernel.Operations;

public interface IAsyncOperation<in TInput, TOutput>
{
    Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}

public interface IAsyncOperation<in TInput>
{
    Task<Result> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}
