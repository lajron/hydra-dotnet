namespace Hydra.Kernel.Primitives;

/// <summary>
/// Represents the category of an application error, such as validation, not found, conflict, forbidden, unauthorized, or internal.
/// </summary>
/// <remarks>
/// This enum is used to classify application errors and provide context for error handling and reporting.
/// </remarks>
public enum AppErrorCategory
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Internal,
}

/// <summary>
/// Represents a validation error for a specific field, including the field name, error code, and any associated parameters.
/// </summary>
/// <remarks>
/// This class is immutable and provides a factory method for creating instances.
/// </remarks>
public sealed class FieldValidationError
{
    public string Field { get; }
    public Enum Code { get; }
    public IReadOnlyList<object?> Parameters { get; }

    private FieldValidationError(string field, Enum code, object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(parameters);

        Field = field;
        Code = code;
        Parameters = Array.AsReadOnly((object?[])parameters.Clone());
    }

    public static FieldValidationError Create(string field, Enum code, params object?[] parameters)
        => new(field, code, parameters);
}

/// <summary>
/// Represents an application error with a specific category, code, parameters, and optional field validation errors.
/// </summary>
/// <remarks>
/// This class is immutable and provides factory methods for creating instances with different error categories.
/// </remarks>
public sealed class AppError
{
    public AppErrorCategory Category { get; }
    public Enum Code { get; }
    public IReadOnlyList<object?> Parameters { get; }
    public IReadOnlyList<FieldValidationError> FieldErrors { get; }

    private AppError(
        AppErrorCategory category,
        Enum code,
        object?[] parameters,
        IEnumerable<FieldValidationError?>? fieldErrors = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(parameters);

        Category = category;
        Code = code;
        Parameters = Array.AsReadOnly((object?[])parameters.Clone());

        if (fieldErrors is null)
        {
            FieldErrors = [];
        }
        else
        {
            var fields = new List<FieldValidationError>();
            foreach (var field in fieldErrors)
            {
                if (field is null)
                {
                    throw new ArgumentException("Field errors cannot contain null.", nameof(fieldErrors));
                }

                fields.Add(field);
            }

            FieldErrors = fields.AsReadOnly();
        }
    }

    /// <summary>
    /// Creates a new AppError instance with the Validation category and the specified code and parameters.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="parameters">The error parameters.</param>
    /// <returns>A new AppError instance.</returns>
    public static AppError Validation(Enum code, params object?[] parameters)
        => new(AppErrorCategory.Validation, code, parameters);

    /// <summary>
    /// Creates a new AppError instance with the Validation category and the specified code and field errors.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="fieldErrors">The field errors.</param>
    /// <param name="parameters">The error parameters.</param>
    /// <returns>A new AppError instance.</returns>
    public static AppError ValidationWithFields(
        Enum code,
        IEnumerable<FieldValidationError?> fieldErrors,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(fieldErrors);
        return new(AppErrorCategory.Validation, code, parameters, fieldErrors);
    }

    /// <summary>
    /// Creates a new AppError instance with the NotFound category and the specified code and parameters.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static AppError NotFound(Enum code, params object?[] parameters)
        => new(AppErrorCategory.NotFound, code, parameters);

    /// <summary>
    /// Creates a new AppError instance with the Conflict category and the specified code and parameters.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="parameters"></param>
    /// <returns>A new AppError instance.</returns>
    public static AppError Conflict(Enum code, params object?[] parameters)
        => new(AppErrorCategory.Conflict, code, parameters);

    /// <summary>
    /// Creates a new AppError instance with the Forbidden category and the specified code and parameters.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="parameters"></param>
    /// <returns>A new AppError instance.</returns>
    public static AppError Forbidden(Enum code, params object?[] parameters)
        => new(AppErrorCategory.Forbidden, code, parameters);

    /// <summary>
    /// Creates a new AppError instance with the Unauthorized category and the specified code and parameters.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="parameters"></param>
    /// <returns>A new AppError instance.</returns>
    public static AppError Unauthorized(Enum code, params object?[] parameters)
        => new(AppErrorCategory.Unauthorized, code, parameters);

    /// <summary>
    /// Creates a new AppError instance with the Internal category and the specified code and parameters.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="parameters"></param>
    /// <returns>A new AppError instance.</returns>
    public static AppError Internal(Enum code, params object?[] parameters)
        => new(AppErrorCategory.Internal, code, parameters);
}
