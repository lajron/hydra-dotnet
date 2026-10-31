namespace Hydra.Kernel.Primitives;

public enum AppErrorCategory
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Internal,
}

public sealed class AppError
{
    public AppErrorCategory Category { get; }
    public Enum Code { get; }
    
    private AppError(AppErrorCategory category, Enum code)
    {
        ArgumentNullException.ThrowIfNull(code);
        
        Category = category;
        Code = code;
    }
    
    public static AppError Validation(Enum code) => new(AppErrorCategory.Validation, code);
    public static AppError NotFound(Enum code) => new(AppErrorCategory.NotFound, code);
    public static AppError Conflict(Enum code) => new(AppErrorCategory.Conflict, code);
    public static AppError Forbidden(Enum code) => new(AppErrorCategory.Forbidden, code);
    public static AppError Unauthorized(Enum code) => new(AppErrorCategory.Unauthorized, code);
    public static AppError Internal(Enum code) => new(AppErrorCategory.Internal, code);
}