using System.Collections.Immutable;

namespace Hydra.Analyzers;

/// <summary>
/// Reports expression-bodied members whose expression itself spans more than one line (a multi-line ternary,
/// LINQ chain or argument list). A single expression wrapped onto the line after <c>=&gt;</c> is accepted.
/// No built-in IDE rule does this: IDE0022 and its siblings only choose between expression and block bodies per member kind.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExpressionBodyAnalyzer : DiagnosticAnalyzer
{
    public const string MultiLineExpressionBodyId = "HYD001";

    internal static readonly DiagnosticDescriptor MultiLineExpressionBody = new(
        MultiLineExpressionBodyId, "Use a block body for multi-line logic",
        "Expression body spans multiple lines; use a block body, or keep the expression on one line (it may start on the line after '=>')",
        "Maintainability", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MultiLineExpressionBody);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ArrowExpressionClause);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var arrow = (ArrowExpressionClauseSyntax)context.Node;
        var span = arrow.SyntaxTree.GetLineSpan(arrow.Expression.Span, context.CancellationToken);
        if (span.StartLinePosition.Line != span.EndLinePosition.Line)
        {
            context.ReportDiagnostic(Diagnostic.Create(MultiLineExpressionBody, arrow.ArrowToken.GetLocation()));
        }
    }
}
