using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hydra.Analyzers.Tests;

public sealed class OperationDependencyTests
{
    private const string Contracts = """
        namespace Hydra.Kernel.Operations
        {
            public interface IAsyncOperation<TInput, TOutput>
            {
                System.Threading.Tasks.Task<TOutput> ExecuteAsync(TInput input);
            }
        }

        namespace Example
        {
            using Hydra.Kernel.Operations;
            public interface IFirstOperation : IAsyncOperation<int, int> { }
            public interface ISecondOperation : IAsyncOperation<int, int> { }
        """;

    [Theory]
    [InlineData("public class First(ISecondOperation other) : IFirstOperation { public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => other.ExecuteAsync(input); }")]
    [InlineData("public class First : IFirstOperation { private ISecondOperation _other = default!; public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => _other.ExecuteAsync(input); }")]
    [InlineData("public class First : IFirstOperation { public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => new Second().ExecuteAsync(input); } public class Second : ISecondOperation { public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => System.Threading.Tasks.Task.FromResult(input); }")]
    [InlineData("public class First(Hydra.Kernel.Operations.IAsyncOperation<int, int> other) : IFirstOperation { public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => other.ExecuteAsync(input); }")]
    public async Task OperationDependencyIsReported(string source)
    {
        var diagnostics = await Analyze(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == OperationDependencyAnalyzer.OperationDependencyId);
    }

    [Theory]
    [InlineData("public class First : IFirstOperation { private readonly Helper _helper = new(); public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => _helper.RunAsync(input); } public class Helper { public System.Threading.Tasks.Task<int> RunAsync(int input) => System.Threading.Tasks.Task.FromResult(input); }")]
    [InlineData("public class First : IFirstOperation { public System.Threading.Tasks.Task<int> ExecuteAsync(int input) => System.Threading.Tasks.Task.FromResult(input); } public class Handler(IFirstOperation operation) { public System.Threading.Tasks.Task<int> Handle(int input) => operation.ExecuteAsync(input); }")]
    [InlineData("public class SubmissionOperation { private readonly RecoveryOperation _other = new(); } public class RecoveryOperation { }")]
    public async Task HelpersHandlersAndUnrelatedTypesAreAccepted(string source)
    {
        var diagnostics = await Analyze(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == OperationDependencyAnalyzer.OperationDependencyId);
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(Contracts + source + "\n}", new CSharpParseOptions(LanguageVersion.CSharp14));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("OperationDependencyTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return await compilation.WithAnalyzers([new OperationDependencyAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }
}
