using System.Collections.Immutable;
using Hydra.Kernel.Primitives;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Hydra.Analyzers.Tests;

public sealed class ResultSafetyTests
{
    private const string Prefix = """
        using System;
        using System.Threading.Tasks;
        using Hydra.Kernel.Primitives;
        enum GuestErrors { NotFound, Conflict }
        readonly struct Token { public int Value { get; init; } }
        sealed class Guest { public string Name { get; set; } = ""; public void UpdateName(string name) => Name = name; }
        sealed class Example
        {
            Result<Guest> Get() => Result.Success(new Guest());
            Result Save() => Result.Success();
            Result<int> GetNumber() => Result.Success(42);
            Result<Guid> GetId() => Result.Success(Guid.NewGuid());
            Result<Token> GetToken() => Result.Success(new Token { Value = 5 });
            Result<Guest?> GetOptional() => Result.Success<Guest?>(null);
            Task<Result<Guest>> GetAsync() => Task.FromResult(Get());
            ValueTask<Result<Guest>> GetValueTaskAsync() => ValueTask.FromResult(Get());
            Task<Result> SaveAsync() => Task.FromResult(Save());
            ValueTask<Result> SaveValueTaskAsync() => ValueTask.FromResult(Save());
            void Process(Guest guest) { }
            void Inspect(in Guest guest) { }
            void Reassign(ref Guest guest) { guest = new Guest(); }
            void AcceptResult(Result<Guest> result) { }
            bool shouldReturn;
            Result<Guest> Execute()
            {
        """;
    private const string Suffix = """
            }
        }
        """;

    [Theory]
    [InlineData("Save(); return Result.Success(new Guest());", "RES001")]
    [InlineData("var r = Save(); return Result.Success(new Guest());", "RES001")]
    [InlineData("var r = Save(); r = Save(); return Result.Success(new Guest());", "RES001")]
    [InlineData("var r = Get(); var copy = r; return Result.Success(new Guest());", "RES001")]
    [InlineData("var r = Get(); var copy = r; r = Get(); return copy;", "RES001")]
    [InlineData("Result<Guest> _ = Get(); var first = _; _ = Get(); return first;", "RES001")]
    [InlineData("var r = Get(); throw new Exception();", "RES001")]
    [InlineData("var (guest, error) = Get(); throw new Exception();", "RES001")]
    [InlineData("AcceptResult(Get()); return Result.Success(new Guest());", "RES001")]
    [InlineData("var values = new[] { Get() }; return Result.Success(new Guest());", "RES001")]
    [InlineData("var (guest, error) = Get(); guest.UpdateName(\"John\"); return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); Process(guest); return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); var copy = guest; return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); guest.Name = \"John\"; return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); var items = new[] { guest }; return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); var text = $\"{guest.Name}\"; return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); var getName = () => guest.Name; return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); string Name() => guest.Name; if (error is not null) return error; return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); var names = System.Linq.Enumerable.Select(new[] { guest }, x => x.Name); if (error is not null) return error; return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); guest = new Guest { Name = guest.Name }; return Result.Success(guest);", "RES002")]
    [InlineData("var (a, aError) = Get(); var (b, bError) = Get(); if (bError is not null) return bError; Process(a); if (aError is not null) return aError; return Result.Success(b);", "RES002")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { Process(guest); return error; } return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); if (error is not null && shouldReturn) return error; return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { if (shouldReturn) return error; } return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { switch(error.Code) { case GuestErrors.NotFound: break; default: return error; } } return Result.Success(guest);", "RES002")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { guest = default!; } return Result.Success(guest);", "RES003")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { guest = null; } return Result.Success(guest);", "RES003")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; guest = null!; return Result.Success(guest);", "RES003")]
    [InlineData("var (guest, error) = Get(); if (error.Code is GuestErrors.NotFound) return error; return Result.Success(guest);", "RES003")]
    [InlineData("var (guest, error) = Get(); error = null; return Result.Success(guest);", "RES003")]
    [InlineData("var (number, error) = GetNumber(); return Result.Success(new Guest { Name = number.ToString() });", "RES002")]
    [InlineData("var (id, error) = GetId(); return Result.Success(new Guest { Name = id.ToString() });", "RES002")]
    [InlineData("var (token, error) = GetToken(); return Result.Success(new Guest { Name = token.Value.ToString() });", "RES002")]
    [InlineData("var (guest, error) = Get(); if ((error = null) is null) return Result.Success(guest); return error!;", "RES003")]
    [InlineData("while (shouldReturn) { var (guest, error) = Get(); if (error is not null) return error; Process(guest); } return Result.Success(new Guest());", "RES003")]
    [InlineData("for (var i = 0; i < 1; i++) { var (guest, error) = Get(); if (error is not null) return error; } return Result.Success(new Guest());", "RES003")]
    [InlineData("foreach (var i in new[] { 1 }) { var (guest, error) = Get(); if (error is not null) return error; } return Result.Success(new Guest());", "RES003")]
    [InlineData("do { var (guest, error) = Get(); if (error is not null) return error; } while (shouldReturn); return Result.Success(new Guest());", "RES003")]
    [InlineData("try { var (guest, error) = Get(); if (error is not null) return error; } catch (Exception) { } return Result.Success(new Guest());", "RES003")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; Reassign(ref guest); return Result.Success(guest);", "RES003")]
    [InlineData("var (guest, error) = Get(); if (error is not null) goto Exit; return Result.Success(guest); Exit: return Result.Success(new Guest());", "RES003")]
    [InlineData("var (guest, error) = Get(); try { if (error is not null) throw new Exception(); } catch (Exception) { } return Result.Success(guest);", "RES003")]
    [InlineData("var (guest, error) = Get(); try { throw new Exception(); } catch (Exception) when (guest.Name.Length > 0) { } return Result.Success(new Guest());", "RES002")]
    [InlineData("var (guest, error) = Get(); try { Process(guest); } finally { } return Result.Success(new Guest());", "RES002")]
    public async Task UnsafeCasesHaveDiagnostic(string body, string id)
    {
        var diagnostics = await Analyze(Prefix + body + Suffix);
        Assert.True(diagnostics.Any(d => d.Id == id && d.Severity == DiagnosticSeverity.Error),
            $"{body}: {string.Join("; ", diagnostics.Select(d => d.ToString()))}");
    }

    [Theory]
    [InlineData("var r = Get(); return r;")]
    [InlineData("var r = Get(); var copy = r; return copy;")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; guest.UpdateName(\"John\"); return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is AppError) return error; return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is not null) throw new Exception(); return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is { Code: GuestErrors.NotFound }) return error; if (error is not null) return error; return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error?.Code is GuestErrors.NotFound) return error; if (error is not null) return error; return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is null) return Result.Success(guest); return error;")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { switch(error.Code) { case GuestErrors.NotFound: return AppError.NotFound(GuestErrors.NotFound); default: return error; } } return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { if (error.Code is not GuestErrors.NotFound) return error; guest = new Guest(); } return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is not null) { if (shouldReturn) guest = new Guest(); else return error; } return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); return error is not null ? error : Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error.Code switch { GuestErrors.NotFound => AppError.NotFound(GuestErrors.NotFound), _ => error }; return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); return error switch { null => Result.Success(guest), _ => error };")]
    [InlineData("var (guest, error) = Get(); switch (error) { case null: return Result.Success(guest); default: return error; }")]
    [InlineData("var (guest, error) = Get(); switch (error) { case AppError known: return known; default: return Result.Success(guest); }")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; var name = nameof(guest); return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; Func<string> getName = () => guest.Name; return Result.Success(new Guest { Name = getName() });")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; Inspect(in guest); return Result.Success(guest);")]
    [InlineData("{ var (guest, error) = Get(); if (error is not null) return error; Process(guest); } { var (guest, error) = Get(); if (error is not null) return error; Process(guest); } return Result.Success(new Guest());")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; (guest, error) = Get(); if (error is not null) return error; return Result.Success(guest);")]
    [InlineData("var (guest, error) = Get(); if (error is not null) return error; var (other, otherError) = Get(); if (otherError is not null) return otherError; Process(guest); return Result.Success(other);")]
    [InlineData("_ = Save(); return Result.Success(new Guest());")]
    [InlineData("var result = Get(); _ = result; return Result.Success(new Guest());")]
    [InlineData("var (_, error) = Save(); if (error is not null) return error; return Result.Success(new Guest());")]
    [InlineData("var (number, error) = GetNumber(); if (error is not null) return error; return Result.Success(new Guest { Name = number.ToString() });")]
    [InlineData("var (id, error) = GetId(); if (error is not null) return error; return Result.Success(new Guest { Name = id.ToString() });")]
    [InlineData("var (token, error) = GetToken(); if (error is not null) return error; return Result.Success(new Guest { Name = token.Value.ToString() });")]
    [InlineData("var (guest, error) = GetOptional(); if (error is not null) return error; return Result.Success(guest ?? new Guest());")]
    [InlineData("var (guest, error) = GetOptional(); if (error is not null) guest = null; return Result.Success(guest ?? new Guest());")]
    [InlineData("var (number, error) = GetNumber(); if (error is not null) number = default; return Result.Success(new Guest { Name = number.ToString() });")]
    public async Task SafeCasesHaveNoDiagnostic(string body)
    {
        var diagnostics = await Analyze(Prefix + body + Suffix);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AwaitedResultCannotBeIgnored()
    {
        var source = Prefix.Replace("Result<Guest> Execute()", "async Task<Result<Guest>> Execute()") +
            "await SaveAsync(); return Result.Success(new Guest());" + Suffix;
        var diagnostics = await Analyze(source);
        Assert.Contains(diagnostics, d => d.Id == "RES001");
    }

    [Fact]
    public async Task ExplicitAwaitedDiscardIsAllowed()
    {
        var source = Prefix.Replace("Result<Guest> Execute()", "async Task<Result<Guest>> Execute()") +
            "_ = await SaveAsync(); return Result.Success(new Guest());" + Suffix;
        Assert.Empty(await Analyze(source));
    }

    [Fact]
    public async Task TaskDiscardWithoutAwaitIsDiagnosed()
    {
        var source = Prefix.Replace("Result<Guest> Execute()", "async Task<Result<Guest>> Execute()") +
            "_ = SaveAsync(); return Result.Success(new Guest());" + Suffix;
        Assert.Contains(await Analyze(source), d => d.Id == "RES001");
    }

    [Theory]
    [InlineData("await GetAsync(); return Result.Success(new Guest());", "RES001")]
    [InlineData("await GetValueTaskAsync(); return Result.Success(new Guest());", "RES001")]
    [InlineData("_ = GetAsync(); return Result.Success(new Guest());", "RES001")]
    [InlineData("var task = GetAsync(); await task; return Result.Success(new Guest());", "RES001")]
    [InlineData("var (guest, error) = await GetAsync(); if (error is not null) return error; return Result.Success(guest);", "")]
    [InlineData("var (guest, error) = await GetValueTaskAsync(); if (error is not null) return error; return Result.Success(guest);", "")]
    [InlineData("return await GetAsync();", "")]
    [InlineData("return await GetValueTaskAsync();", "")]
    [InlineData("var task = GetAsync(); return await task;", "")]
    [InlineData("var task = Task.FromResult(Get()); return await task;", "")]
    [InlineData("var task = GetAsync(); var (guest, error) = await task; if (error is not null) return error; return Result.Success(guest);", "")]
    [InlineData("var task = GetAsync(); _ = await task; return Result.Success(new Guest());", "")]
    public async Task AsyncResultCases(string body, string expectedId)
    {
        var source = Prefix.Replace("Result<Guest> Execute()", "async Task<Result<Guest>> Execute()") + body + Suffix;
        var diagnostics = await Analyze(source);
        if (expectedId.Length == 0) Assert.Empty(diagnostics);
        else Assert.Contains(diagnostics, d => d.Id == expectedId);
    }

    [Fact]
    public async Task UnrelatedResultIsIgnored()
    {
        var source = "sealed class Result { } sealed class Example { Result Get() => new(); void Execute() { Get(); } }";
        Assert.Empty(await Analyze(source));
    }

    [Fact]
    public async Task DiagnosticHasActionableMessageAndLocation()
    {
        var source = Prefix + "var (guest, error) = Get(); Process(guest); return Result.Success(new Guest());" + Suffix;
        var diagnostic = Assert.Single((await Analyze(source)).Where(d => d.Id == "RES002"));
        Assert.Equal("guest", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Contains("before its associated error is handled", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task IgnoredResultDiagnosticPointsToTheCall()
    {
        var source = Prefix + "Save(); return Result.Success(new Guest());" + Suffix;
        var diagnostic = Assert.Single((await Analyze(source)).Where(d => d.Id == "RES001"));
        Assert.Equal("Save()", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Contains("outcome is ignored", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task UnsafeFlowDiagnosticPointsToErrorMutation()
    {
        var source = Prefix + "var (guest, error) = Get(); error = null; if (error is not null) return error; return Result.Success(guest);" + Suffix;
        var diagnostic = Assert.Single((await Analyze(source)).Where(d => d.Id == "RES003"));
        Assert.Equal("error", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.Contains("safety cannot be proven", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task LambdaAndLocalFunctionBodiesAreAnalyzed()
    {
        var source = Prefix + "Action action = () => Save(); void Inner() { Save(); } return Result.Success(new Guest());" + Suffix;
        var diagnostics = await Analyze(source);
        Assert.Equal(2, diagnostics.Count(d => d.Id == "RES001"));
    }

    [Fact]
    public async Task ExpressionBodiedMethodCannotDiscardResult()
    {
        var source = Prefix + "return Result.Success(new Guest());" + Suffix +
            "sealed class Other { Result Save() => Result.Success(); void Run() => Save(); }";
        Assert.Contains(await Analyze(source), d => d.Id == "RES001");
    }

    [Fact]
    public async Task ConstructorAndAccessorBodiesAreAnalyzed()
    {
        const string source = """
            using Hydra.Kernel.Primitives;
            sealed class Other
            {
                Result Save() => Result.Success();
                public Other() { Save(); }
                int Property { set { Save(); } }
            }
            """;
        Assert.Equal(2, (await Analyze(source)).Count(d => d.Id == "RES001"));
    }

    [Fact]
    public async Task ExpressionBodiedConstructorAndSetterAreAnalyzed()
    {
        const string source = """
            using Hydra.Kernel.Primitives;
            sealed class Other
            {
                Result Save() => Result.Success();
                public Other() => Save();
                int Property { set => Save(); }
            }
            """;
        Assert.Equal(2, (await Analyze(source)).Count(d => d.Id == "RES001"));
    }

    [Fact]
    public async Task TopLevelStatementsAreAnalyzed()
    {
        const string source = "using Hydra.Kernel.Primitives; Result.Success();";
        Assert.Contains(await Analyze(source, OutputKind.ConsoleApplication), d => d.Id == "RES001");
    }

    [Fact]
    public async Task ResultStoredAcrossMethodsIsExplicitlyUnsupported()
    {
        const string source = """
            using Hydra.Kernel.Primitives;
            sealed class Store
            {
                static Result<Guest> Get() => Result.Success(new Guest());
                readonly Result<Guest> field = Get();
                Result<Guest> Property { get; } = Get();
            }
            sealed class Guest { }
            """;
        Assert.Equal(2, (await Analyze(source)).Count(d => d.Id == "RES003"));
    }

    [Fact]
    public async Task KnownSuccessCanBeStoredAsAField()
    {
        const string source = """
            using Hydra.Kernel.Primitives;
            sealed class Store
            {
                readonly Result<int> field = Result.Success(1);
            }
            """;
        Assert.Empty(await Analyze(source));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(
        string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Result).Assembly.Location));
        var compilation = CSharpCompilation.Create("AnalyzerTest", [tree], references,
            new CSharpCompilationOptions(outputKind, nullableContextOptions: NullableContextOptions.Enable));
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(compilerErrors.Length == 0, string.Join(Environment.NewLine, compilerErrors.Select(d => d.ToString())));
        return await compilation.WithAnalyzers([new ResultSafetyAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }
}
