using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hydra.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultSafetyAnalyzer : DiagnosticAnalyzer
{
    public const string IgnoredResultId = "RES001";
    public const string UnsafeValueId = "RES002";
    public const string UnsafeFlowId = "RES003";

    internal static readonly DiagnosticDescriptor IgnoredResult = new(
        IgnoredResultId, "Handle the Result outcome",
        "Result outcome is ignored; handle its error or explicitly discard the Result (await asynchronous calls first)",
        "ResultSafety", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsafeValue = new(
        UnsafeValueId, "Result value may be invalid",
        "Result value '{0}' may be used before its associated error is handled or a replacement is assigned",
        "ResultSafety", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsafeFlow = new(
        UnsafeFlowId, "Result safety is not proven",
        "Result safety cannot be proven here; propagate the failure, assign a safe replacement, or simplify the flow",
        "ResultSafety", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(IgnoredResult, UnsafeValue, UnsafeFlow);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var result = start.Compilation.GetTypeByMetadataName("Hydra.Kernel.Primitives.Result");
            var genericResult = start.Compilation.GetTypeByMetadataName("Hydra.Kernel.Primitives.Result`1");
            if (result is null || genericResult is null ||
                result.ContainingAssembly.Name != "Hydra.Kernel" ||
                genericResult.ContainingAssembly.Name != "Hydra.Kernel")
                return;

            start.RegisterSyntaxNodeAction(c => Analyze(c, result, genericResult), SyntaxKind.MethodDeclaration);
            start.RegisterSyntaxNodeAction(c => AnalyzeOtherBody(c, result, genericResult),
                SyntaxKind.ConstructorDeclaration, SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.SetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration,
                SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration,
                SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration,
                SyntaxKind.CompilationUnit);
            start.RegisterSyntaxNodeAction(c => AnalyzeStorage(c, result, genericResult),
                SyntaxKind.FieldDeclaration, SyntaxKind.PropertyDeclaration);
            start.RegisterSyntaxNodeAction(c => AnalyzeNested(c, result, genericResult),
                SyntaxKind.LocalFunctionStatement, SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
        });
    }

    private static void AnalyzeNested(SyntaxNodeAnalysisContext context, INamedTypeSymbol result, INamedTypeSymbol genericResult)
    {
        BlockSyntax? body = context.Node switch
        {
            LocalFunctionStatementSyntax local => local.Body,
            SimpleLambdaExpressionSyntax lambda => lambda.Body as BlockSyntax,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.Body as BlockSyntax,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            _ => null,
        };
        if (body is not null)
        {
            new ResultFlowAnalysis(context, result, genericResult).Analyze(body);
            return;
        }

        ExpressionSyntax? expression = context.Node switch
        {
            SimpleLambdaExpressionSyntax lambda => lambda.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.Body as ExpressionSyntax,
            _ => null,
        };
        if (expression is null) return;
        var converted = context.SemanticModel.GetTypeInfo(context.Node).ConvertedType as INamedTypeSymbol;
        if (converted?.DelegateInvokeMethod?.ReturnsVoid != true) return;
        var type = context.SemanticModel.GetTypeInfo(expression).Type;
        if (type is null) return;
        if (IsResultType(type, result, genericResult))
            context.ReportDiagnostic(Diagnostic.Create(IgnoredResult, expression.GetLocation()));
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol result, INamedTypeSymbol genericResult)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body is null)
        {
            if (method.ExpressionBody is not null)
            {
                var expression = method.ExpressionBody.Expression;
                var type = context.SemanticModel.GetTypeInfo(expression).Type;
                var returned = context.SemanticModel.GetDeclaredSymbol(method)?.ReturnType;
                if (IsResultOrTaskType(type, result, genericResult) &&
                    !SymbolEqualityComparer.Default.Equals(type, returned) &&
                    !IsResultType(returned, result, genericResult))
                    context.ReportDiagnostic(Diagnostic.Create(IgnoredResult, expression.GetLocation()));
            }
            return;
        }

        var analysis = new ResultFlowAnalysis(context, result, genericResult);
        analysis.Analyze(method.Body);
    }

    private static void AnalyzeOtherBody(SyntaxNodeAnalysisContext context, INamedTypeSymbol result, INamedTypeSymbol genericResult)
    {
        var flow = new ResultFlowAnalysis(context, result, genericResult);
        switch (context.Node)
        {
            case ConstructorDeclarationSyntax constructor when constructor.Body is not null:
                flow.Analyze(constructor.Body);
                break;
            case AccessorDeclarationSyntax accessor when accessor.Body is not null:
                flow.Analyze(accessor.Body);
                break;
            case OperatorDeclarationSyntax operation when operation.Body is not null:
                flow.Analyze(operation.Body);
                break;
            case ConversionOperatorDeclarationSyntax conversion when conversion.Body is not null:
                flow.Analyze(conversion.Body);
                break;
            case CompilationUnitSyntax unit:
                var statements = unit.Members.OfType<GlobalStatementSyntax>().Select(x => x.Statement).ToArray();
                if (statements.Length > 0) flow.Analyze(statements);
                break;
        }

        ArrowExpressionClauseSyntax? arrow = context.Node switch
        {
            ConstructorDeclarationSyntax constructor => constructor.ExpressionBody,
            AccessorDeclarationSyntax accessor => accessor.ExpressionBody,
            OperatorDeclarationSyntax operation => operation.ExpressionBody,
            ConversionOperatorDeclarationSyntax conversion => conversion.ExpressionBody,
            _ => null,
        };
        if (arrow is not null)
        {
            var type = context.SemanticModel.GetTypeInfo(arrow.Expression).Type;
            var returned = (context.SemanticModel.GetDeclaredSymbol(context.Node) as IMethodSymbol)?.ReturnType;
            if (IsResultOrTaskType(type, result, genericResult) &&
                !SymbolEqualityComparer.Default.Equals(type, returned) &&
                !IsResultType(returned, result, genericResult))
                context.ReportDiagnostic(Diagnostic.Create(IgnoredResult, arrow.Expression.GetLocation()));
        }
    }

    private static bool IsResultType(ITypeSymbol? type, INamedTypeSymbol result, INamedTypeSymbol genericResult) =>
        SymbolEqualityComparer.Default.Equals(type, result) ||
        type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, genericResult);

    private static bool IsResultOrTaskType(ITypeSymbol? type, INamedTypeSymbol result, INamedTypeSymbol genericResult) =>
        IsResultType(type, result, genericResult) ||
        type is INamedTypeSymbol named && named.TypeArguments.Length == 1 &&
        named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
        (named.Name == "Task" || named.Name == "ValueTask") &&
        IsResultType(named.TypeArguments[0], result, genericResult);

    private static void AnalyzeStorage(SyntaxNodeAnalysisContext context, INamedTypeSymbol result, INamedTypeSymbol genericResult)
    {
        var initializers = context.Node switch
        {
            FieldDeclarationSyntax field => field.Declaration.Variables
                .Where(variable => variable.Initializer is not null)
                .Select(variable => variable.Initializer!.Value),
            PropertyDeclarationSyntax property when property.Initializer is not null =>
                new[] { property.Initializer.Value }.AsEnumerable(),
            _ => Enumerable.Empty<ExpressionSyntax>(),
        };
        foreach (var initializer in initializers)
            if (IsResultOrTaskType(context.SemanticModel.GetTypeInfo(initializer).Type, result, genericResult) &&
                !IsKnownSuccess(initializer, context.SemanticModel, result))
                context.ReportDiagnostic(Diagnostic.Create(UnsafeFlow, initializer.GetLocation()));
    }

    private static bool IsKnownSuccess(ExpressionSyntax expression, SemanticModel model, INamedTypeSymbol result) =>
        expression is InvocationExpressionSyntax invocation &&
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
        method.IsStatic && method.Name == "Success" &&
        SymbolEqualityComparer.Default.Equals(method.ContainingType, result);
}
