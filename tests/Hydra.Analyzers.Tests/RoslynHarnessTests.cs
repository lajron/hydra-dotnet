using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Hydra.Analyzers.Tests;

public sealed class RoslynHarnessTests
{
    [Fact]
    public async Task AnalyzerDoesNotAffectOrdinaryCSharpProjects()
    {
        var test = new CSharpAnalyzerTest<ResultSafetyAnalyzer, DefaultVerifier>
        {
            TestCode = "sealed class Example { int Add(int left, int right) => left + right; }",
        };
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
