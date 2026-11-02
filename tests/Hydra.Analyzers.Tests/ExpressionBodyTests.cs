using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hydra.Analyzers.Tests;

public sealed class ExpressionBodyTests
{
    [Theory]
    [InlineData("int M(int x) => x > 0\n ? 1\n : 2;")]
    [InlineData("int M(int x) =>\n x > 0\n ? 1\n : 2;")]
    [InlineData("int M(int[] xs) => xs\n .Where(x => x > 0)\n .Sum();")]
    [InlineData("int P => 1 +\n 2;")]
    [InlineData("int this[int i] => i +\n 1;")]
    [InlineData("int P { get => 1 +\n 2; }")]
    [InlineData("C() => M(1,\n 2);")]
    [InlineData("static C operator +(C a, C b) => new C(\n);")]
    [InlineData("int M() { int Local() => 1 +\n 2; return Local(); }")]
    public async Task MultiLineExpressionBodyIsReported(string member)
    {
        var diagnostics = await Analyze(Wrap(member));
        Assert.Contains(diagnostics, d => d.Id == "HYD001");
    }

    [Theory]
    [InlineData("int M(int x) => x + 1;")]
    [InlineData("int M(int x) =>\n x + 1;")]
    [InlineData("int M(int x) =>\n x > 0 ? 1 : 2;")]
    [InlineData("int P => 1;")]
    [InlineData("int P =>\n 1;")]
    [InlineData("int M(int x)\n{\n return x > 0\n ? 1\n : 2;\n}")]
    [InlineData("int M(int[] xs)\n{\n return xs\n .Where(x => x > 0)\n .Sum();\n}")]
    [InlineData("System.Func<int, int> F = x =>\n x + 1;")]
    public async Task SingleLineExpressionBodiesAndBlocksAreAccepted(string member)
    {
        var diagnostics = await Analyze(Wrap(member));
        Assert.DoesNotContain(diagnostics, d => d.Id == "HYD001");
    }

    private static string Wrap(string member) =>
        "using System.Linq;\nclass C\n{\n" + member + "\n}\n";

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ExpressionBodyTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return await compilation.WithAnalyzers([new ExpressionBodyAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }
}
