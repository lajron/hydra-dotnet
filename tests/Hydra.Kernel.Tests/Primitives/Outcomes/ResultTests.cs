using Hydra.Kernel.Primitives;
using Xunit;

namespace Hydra.Kernel.Tests.Primitives.Outcomes;

public sealed class ResultTests
{
    private enum ErrorCode { Missing }

    [Fact]
    public void NonGenericSuccessDeconstructsWithoutError()
    {
        var result = Result.Success();

        result.Deconstruct(out var succeeded, out var error);

        Assert.True(succeeded);
        Assert.Null(error);
    }

    [Fact]
    public void NonGenericFailurePreservesTheErrorInstance()
    {
        var expected = AppError.NotFound(ErrorCode.Missing);
        Result result = expected;

        result.Deconstruct(out var succeeded, out var error);

        Assert.False(succeeded);
        Assert.Same(expected, error);
    }

    [Fact]
    public void GenericSuccessPreservesTheValueInstance()
    {
        var expected = new object();
        Result<object> result = Result.Success(expected);

        result.Deconstruct(out var value, out var error);

        Assert.Same(expected, value);
        Assert.Null(error);
    }

    [Fact]
    public void GenericSuccessAllowsNullAsAValue()
    {
        Result<string?> result = Result.Success<string?>(null);

        result.Deconstruct(out var value, out var error);

        Assert.Null(value);
        Assert.Null(error);
    }

    [Fact]
    public void GenericFailurePreservesErrorAndReturnsDefaultValue()
    {
        var expected = AppError.NotFound(ErrorCode.Missing);
        Result<int> result = expected;

        result.Deconstruct(out var value, out var error);

        Assert.Equal(0, value);
        Assert.Same(expected, error);
    }

    [Fact]
    public void NullErrorCannotBecomeNonGenericFailure()
    {
        AppError error = null!;

        Assert.Throws<ArgumentNullException>(() => { Result result = error; _ = result; });
    }

    [Fact]
    public void NullErrorCannotBecomeGenericFailure()
    {
        AppError error = null!;

        Assert.Throws<ArgumentNullException>(() => { Result<int> result = error; _ = result; });
    }
}
