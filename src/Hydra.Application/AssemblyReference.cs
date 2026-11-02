namespace Hydra.Application;

/// <summary>Identifies the application assembly for compile-time service discovery.</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
