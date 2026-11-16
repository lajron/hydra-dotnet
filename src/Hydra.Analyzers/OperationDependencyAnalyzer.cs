using System.Collections.Immutable;
using System.Linq;

namespace Hydra.Analyzers;

/// <summary>
/// Prevents Hydra Operations from depending on another Operation.
/// Operation identity is resolved from the Kernel contract, not a filename or namespace.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperationDependencyAnalyzer : DiagnosticAnalyzer
{
    public const string OperationDependencyId = "HYD002";

    internal static readonly DiagnosticDescriptor OperationDependency = new(
        OperationDependencyId, "Operations cannot depend on other Operations",
        "Operation '{0}' depends on Operation '{1}'; orchestrate them in an Application Handler",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(OperationDependency);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var withOutput = start.Compilation.GetTypeByMetadataName("Hydra.Kernel.Operations.IAsyncOperation`2");
            var withoutOutput = start.Compilation.GetTypeByMetadataName("Hydra.Kernel.Operations.IAsyncOperation`1");
            if (withOutput is null && withoutOutput is null)
            {
                return;
            }

            bool IsContract(INamedTypeSymbol type) =>
                SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, withOutput) ||
                SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, withoutOutput);

            bool IsOperation(INamedTypeSymbol type) => IsContract(type) || type.AllInterfaces.Any(IsContract);

            start.RegisterSymbolAction(symbolContext =>
            {
                var owner = (INamedTypeSymbol)symbolContext.Symbol;
                if (!IsOperation(owner))
                {
                    return;
                }

                foreach (var member in owner.GetMembers())
                {
                    switch (member)
                    {
                        case IFieldSymbol field when field.Type is INamedTypeSymbol type && IsOperation(type):
                            Report(symbolContext, field, owner, type);
                            break;
                        case IPropertySymbol property when property.Type is INamedTypeSymbol type && IsOperation(type):
                            Report(symbolContext, property, owner, type);
                            break;
                        case IMethodSymbol method:
                            foreach (var parameter in method.Parameters)
                            {
                                if (parameter.Type is INamedTypeSymbol type && IsOperation(type))
                                {
                                    Report(symbolContext, parameter, owner, type);
                                }
                            }

                            break;
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterSyntaxNodeAction(nodeContext =>
            {
                var invocation = (InvocationExpressionSyntax)nodeContext.Node;
                var owner = nodeContext.ContainingSymbol?.ContainingType;
                if (owner is null || !IsOperation(owner) ||
                    nodeContext.SemanticModel.GetSymbolInfo(invocation, nodeContext.CancellationToken).Symbol is not IMethodSymbol method ||
                    method.ContainingType is not { } target || !IsOperation(target) ||
                    SymbolEqualityComparer.Default.Equals(owner, target))
                {
                    return;
                }

                nodeContext.ReportDiagnostic(Diagnostic.Create(OperationDependency,
                    invocation.GetLocation(), owner.Name, target.Name));
            }, SyntaxKind.InvocationExpression);

            start.RegisterSyntaxNodeAction(nodeContext =>
            {
                var creation = (ObjectCreationExpressionSyntax)nodeContext.Node;
                var owner = nodeContext.ContainingSymbol?.ContainingType;
                if (owner is null || !IsOperation(owner) ||
                    nodeContext.SemanticModel.GetTypeInfo(creation, nodeContext.CancellationToken).Type is not INamedTypeSymbol target ||
                    !IsOperation(target) || SymbolEqualityComparer.Default.Equals(owner, target))
                {
                    return;
                }

                nodeContext.ReportDiagnostic(Diagnostic.Create(OperationDependency,
                    creation.GetLocation(), owner.Name, target.Name));
            }, SyntaxKind.ObjectCreationExpression);
        });
    }

    private static void Report(SymbolAnalysisContext context, ISymbol dependency,
        INamedTypeSymbol owner, INamedTypeSymbol target)
    {
        var location = dependency.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(OperationDependency, location, owner.Name, target.Name));
        }
    }
}
