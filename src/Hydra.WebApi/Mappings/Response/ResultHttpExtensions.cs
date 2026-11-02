namespace Hydra.WebApi.Mappings.Response;

// This is the HTTP boundary: a failure is converted to IActionResult rather than
// propagated as another Result, which the application result analyzer cannot model.
#pragma warning disable RES003
public static class ResultHttpExtensions
{
    public static IActionResult ToHttpResponse(this Result result, int successStatusCode = StatusCodes.Status204NoContent)
    {
        var (_, error) = result;
        if (error is not null)
        {
            return ToProblem(error);
        }

        return new StatusCodeResult(successStatusCode);
    }

    public static IActionResult ToHttpResponse<T>(this Result<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        var (value, error) = result;
        if (error is not null)
        {
            return ToProblem(error);
        }

        return new ObjectResult(value) { StatusCode = successStatusCode };
    }

    private static IActionResult ToProblem(AppError error)
    {
        var status = error.Category switch
        {
            AppErrorCategory.Validation => StatusCodes.Status400BadRequest,
            AppErrorCategory.Unauthorized => StatusCodes.Status401Unauthorized,
            AppErrorCategory.Forbidden => StatusCodes.Status403Forbidden,
            AppErrorCategory.NotFound => StatusCodes.Status404NotFound,
            AppErrorCategory.Conflict => StatusCodes.Status409Conflict,
            AppErrorCategory.Internal => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError,
        };

        ProblemDetails problem;
        if (error.Category == AppErrorCategory.Validation)
        {
            var errors = error.FieldErrors
                .GroupBy(field => field.Field)
                .ToDictionary(group => group.Key, group => group.Select(GetFieldMessage).ToArray());

            problem = new ValidationProblemDetails(errors);
        }
        else
        {
            problem = new ProblemDetails();
        }

        problem.Status = status;
        problem.Title = $"{error.Code.GetType().Name}.{error.Code}";
        problem.Detail = EnumErrorLocalizer.GetMessage(error.Code, error.Parameters);

        ObjectResult response = status switch
        {
            StatusCodes.Status400BadRequest => new BadRequestObjectResult(problem),
            StatusCodes.Status401Unauthorized => new UnauthorizedObjectResult(problem),
            StatusCodes.Status404NotFound => new NotFoundObjectResult(problem),
            StatusCodes.Status409Conflict => new ConflictObjectResult(problem),
            _ => new ObjectResult(problem) { StatusCode = status },
        };

        response.ContentTypes.Add("application/problem+json");
        return response;
    }

    private static string GetFieldMessage(FieldValidationError field)
    {
        if (field.Code is ValidationErrorCode.InvalidField && field.Parameters.Count >= 2)
        {
            return field.Parameters[1] as string ?? string.Empty;
        }

        return EnumErrorLocalizer.GetMessage(field.Code, field.Parameters);
    }
}
