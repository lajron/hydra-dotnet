using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Hydra.Analyzers;

// Path-sensitive analysis for ordinary blocks, conditionals, and switches. Unsupported
// exception/loop flow is diagnosed at its Result operation instead of assumed safe.
internal sealed class ResultFlowAnalysis
{
    private readonly SyntaxNodeAnalysisContext _context;
    private readonly SemanticModel _model;
    private readonly INamedTypeSymbol _result;
    private readonly INamedTypeSymbol _genericResult;
    private readonly INamedTypeSymbol? _appError;
    private readonly HashSet<string> _reported = new();
    private readonly HashSet<Pair> _unsafePairs = new();

    private sealed class Pair
    {
        internal Pair(ISymbol? value, ISymbol error, Location location, ITypeSymbol? successType)
        {
            Value = value;
            Error = error;
            Location = location;
            SuccessType = successType;
        }

        internal ISymbol? Value { get; }
        internal ISymbol Error { get; }
        internal Location Location { get; }
        internal ITypeSymbol? SuccessType { get; }
    }

    private enum Outcome { Unknown, Success, Failure, Recovered }

    private sealed class State
    {
        internal Dictionary<Pair, Outcome> Pairs { get; } = new();
        internal Dictionary<ISymbol, Location> Pending { get; } = new(SymbolEqualityComparer.Default);
        internal bool Break { get; set; }

        internal State Copy()
        {
            var copy = new State { Break = Break };
            foreach (var entry in Pairs) copy.Pairs.Add(entry.Key, entry.Value);
            foreach (var entry in Pending) copy.Pending.Add(entry.Key, entry.Value);
            return copy;
        }
    }

    internal ResultFlowAnalysis(SyntaxNodeAnalysisContext context, INamedTypeSymbol result, INamedTypeSymbol genericResult)
    {
        _context = context;
        _model = context.SemanticModel;
        _result = result;
        _genericResult = genericResult;
        _appError = context.Compilation.GetTypeByMetadataName("Hydra.Kernel.Primitives.AppError");
    }

    internal void Analyze(BlockSyntax body)
    {
        var exits = RunStatements(body.Statements, new List<State> { new() });
        foreach (var state in exits)
            ReportUnfinished(state, body.CloseBraceToken.GetLocation());
    }

    internal void Analyze(IEnumerable<StatementSyntax> statements)
    {
        var list = statements.ToArray();
        if (list.Length == 0) return;
        var exits = RunStatements(list, new List<State> { new() });
        foreach (var state in exits)
            ReportUnfinished(state, list[list.Length - 1].GetLocation());
    }

    private List<State> RunStatements(IEnumerable<StatementSyntax> statements, List<State> incoming)
    {
        var current = incoming;
        foreach (var statement in statements)
        {
            if (current.Count == 0) break;
            var next = new List<State>();
            foreach (var state in current)
            {
                if (state.Break) { next.Add(state); continue; }
                next.AddRange(Run(statement, state));
            }
            current = MergeEquivalent(next);
            if (current.Count > 64)
            {
                Report(ResultSafetyAnalyzer.UnsafeFlow, statement.GetLocation());
                return new List<State>();
            }
        }
        return current;
    }

    private List<State> Run(StatementSyntax statement, State state)
    {
        switch (statement)
        {
            case BlockSyntax block:
                return RunStatements(block.Statements, new List<State> { state });
            case LocalDeclarationStatementSyntax declaration:
                foreach (var variable in declaration.Declaration.Variables)
                {
                    if (variable.Initializer is null) continue;
                    Read(variable.Initializer.Value, state);
                    var symbol = _model.GetDeclaredSymbol(variable);
                    var source = OutcomeSource(variable.Initializer.Value);
                    if (symbol is not null && source is not null && state.Pending.TryGetValue(source, out var obligation))
                    {
                        if (variable.Initializer.Value is AwaitExpressionSyntax)
                        {
                            ClearPending(state, source);
                            state.Pending[symbol] = variable.GetLocation();
                        }
                        else
                            state.Pending[symbol] = obligation;
                        continue;
                    }
                    if (IsResultOrTask(_model.GetTypeInfo(variable.Initializer.Value).Type))
                    {
                        if (symbol is not null) state.Pending[symbol] = variable.GetLocation();
                    }
                }
                return One(state);
            case ExpressionStatementSyntax expressionStatement:
                return RunExpressionStatement(expressionStatement, state);
            case IfStatementSyntax conditional:
                return RunIf(conditional, state);
            case SwitchStatementSyntax switchStatement:
                return RunSwitch(switchStatement, state);
            case ReturnStatementSyntax returned:
                RunReturn(returned.Expression, state, returned);
                return new List<State>();
            case ThrowStatementSyntax thrown:
                if (thrown.Expression is not null) Read(thrown.Expression, state);
                ReportUnobservedAtThrow(state);
                return new List<State>();
            case BreakStatementSyntax:
                state.Break = true;
                return One(state);
            case EmptyStatementSyntax:
                return One(state);
            case LocalFunctionStatementSyntax localFunction:
                Read(localFunction, state);
                return One(state);
            default:
                Read(statement, state);
                if (ContainsResultWork(statement) || state.Pairs.Values.Any(x => x is Outcome.Unknown or Outcome.Failure))
                    Report(ResultSafetyAnalyzer.UnsafeFlow, statement.GetLocation());
                // This statement's control flow is outside the supported language subset.
                // Its Result operations have a diagnostic; never infer success from it.
                return One(state);
        }
    }

    private void RunReturn(ExpressionSyntax? expression, State state, ReturnStatementSyntax returned)
    {
        if (expression is ConditionalExpressionSyntax conditional)
        {
            var (yes, no) = Condition(conditional.Condition, state);
            foreach (var path in yes) RunReturn(conditional.WhenTrue, path, returned);
            foreach (var path in no) RunReturn(conditional.WhenFalse, path, returned);
            return;
        }
        if (expression is ThrowExpressionSyntax thrown)
        {
            Read(thrown.Expression, state);
            ReportUnobservedAtThrow(state);
            return;
        }
        if (expression is SwitchExpressionSyntax switched)
        {
            Read(switched.GoverningExpression, state);
            var pair = FindErrorPair(switched.GoverningExpression, state);
            var hasNullArm = switched.Arms.Any(arm => IsNullPattern(arm.Pattern));
            var hasDiscard = switched.Arms.Any(arm => arm.Pattern is DiscardPatternSyntax);
            if (!hasDiscard)
                Report(ResultSafetyAnalyzer.UnsafeFlow, switched.GetLocation());
            foreach (var arm in switched.Arms)
            {
                var path = state.Copy();
                if (pair is not null)
                {
                    if (IsNullPattern(arm.Pattern)) path.Pairs[pair] = Outcome.Success;
                    else if (arm.Pattern is not DiscardPatternSyntax || hasNullArm)
                        path.Pairs[pair] = Outcome.Failure;
                }
                if (arm.WhenClause is not null) Read(arm.WhenClause.Condition, path);
                RunReturn(arm.Expression, path, returned);
            }
            return;
        }
        if (expression is not null)
        {
            Read(expression, state);
            var returnedSymbol = OutcomeSource(expression);
            if (returnedSymbol is not null) ClearPending(state, returnedSymbol);
        }
        CheckReturn(state, returned, expression);
    }

    private List<State> RunExpressionStatement(ExpressionStatementSyntax statement, State state)
    {
        var expression = statement.Expression;
        if (expression is AssignmentExpressionSyntax assignment &&
            _model.GetOperation(assignment) is IDeconstructionAssignmentOperation deconstruction &&
            IsResult(deconstruction.Value.Type))
        {
            AddPair(assignment, state);
            return One(state);
        }

        if (expression is AssignmentExpressionSyntax assign)
        {
            Read(assign.Right, state);
            if (assign.Left is not IdentifierNameSyntax)
                Read(assign.Left, state);
            var target = Symbol(assign.Left);
            if (assign.Left is IdentifierNameSyntax id && id.Identifier.ValueText == "_" &&
                _model.GetOperation(assign.Left) is IDiscardOperation)
            {
                if (IsResultOrTask(_model.GetTypeInfo(assign.Right).Type) &&
                    !IsResult(_model.GetTypeInfo(assign.Right).Type))
                    Report(ResultSafetyAnalyzer.IgnoredResult, assign.GetLocation());
                var discarded = OutcomeSource(assign.Right);
                if (discarded is not null) ClearPending(state, discarded);
                return One(state);
            }

            if (target is not null)
            {
                var source = OutcomeSource(assign.Right);
                Location? sourceObligation = null;
                if (source is not null) state.Pending.TryGetValue(source, out sourceObligation);
                if (state.Pending.TryGetValue(target, out var old))
                {
                    state.Pending.Remove(target);
                    if (!ReferenceEquals(sourceObligation, old) &&
                        !state.Pending.Values.Any(location => ReferenceEquals(location, old)))
                        Report(ResultSafetyAnalyzer.IgnoredResult, old);
                }
                if (sourceObligation is not null)
                    state.Pending[target] = sourceObligation;
                else if (IsResultOrTask(_model.GetTypeInfo(assign.Right).Type))
                    state.Pending[target] = assign.GetLocation();

                foreach (var pair in state.Pairs.Keys.ToArray())
                {
                    if (SymbolEqualityComparer.Default.Equals(pair.Error, target))
                        Report(ResultSafetyAnalyzer.UnsafeFlow, assign.Left.GetLocation());
                    if (pair.Value is not null && SymbolEqualityComparer.Default.Equals(pair.Value, target))
                    {
                        if (IsInvalidFallback(pair, assign.Right))
                            Report(ResultSafetyAnalyzer.UnsafeFlow, assign.Right.GetLocation());
                        else if (state.Pairs[pair] == Outcome.Failure)
                            state.Pairs[pair] = Outcome.Recovered;
                    }
                }
            }
            return One(state);
        }

        Read(expression, state);
        if (IsResultOrTask(_model.GetTypeInfo(expression).Type))
            Report(ResultSafetyAnalyzer.IgnoredResult, expression.GetLocation());
        var awaitedSource = OutcomeSource(expression);
        if (expression is AwaitExpressionSyntax && awaitedSource is not null)
            ClearPending(state, awaitedSource);
        return One(state);
    }

    private void AddPair(AssignmentExpressionSyntax assignment, State state)
    {
        var slots = new List<ISymbol?>();
        foreach (var node in assignment.Left.DescendantNodesAndSelf())
        {
            if (node is SingleVariableDesignationSyntax designation)
                slots.Add(_model.GetDeclaredSymbol(designation));
            else if (node is DiscardDesignationSyntax)
                slots.Add(null);
            else if (node is IdentifierNameSyntax identifier &&
                     !identifier.Ancestors().Any(a => a is DeclarationExpressionSyntax))
                slots.Add(Symbol(identifier));
        }
        if (slots.Count != 2 || slots[1] is null)
        {
            Report(ResultSafetyAnalyzer.UnsafeFlow, assignment.Left.GetLocation());
            return;
        }

        // Reusing either slot must not erase a prior unhandled outcome.
        foreach (var old in state.Pairs.Keys.ToArray())
        {
            if (slots.Any(slot => slot is not null &&
                (SymbolEqualityComparer.Default.Equals(slot, old.Value) ||
                 SymbolEqualityComparer.Default.Equals(slot, old.Error))))
            {
                if (state.Pairs[old] is Outcome.Unknown or Outcome.Failure)
                    Report(ResultSafetyAnalyzer.IgnoredResult, old.Location);
                state.Pairs.Remove(old);
            }
        }

        var source = OutcomeSource(assignment.Right);
        if (source is not null) ClearPending(state, source);
        var deconstruction = (IDeconstructionAssignmentOperation)_model.GetOperation(assignment)!;
        var successType = (deconstruction.Value.Type as INamedTypeSymbol)?.TypeArguments.FirstOrDefault();
        state.Pairs.Add(new Pair(slots[0], slots[1]!, assignment.GetLocation(), successType), Outcome.Unknown);
    }

    private List<State> RunIf(IfStatementSyntax statement, State state)
    {
        var (yes, no) = Condition(statement.Condition, state);
        var result = new List<State>();
        foreach (var path in yes) result.AddRange(Run(statement.Statement, path));
        foreach (var path in no)
        {
            if (statement.Else is null) result.Add(path);
            else result.AddRange(Run(statement.Else.Statement, path));
        }
        return result;
    }

    private List<State> RunSwitch(SwitchStatementSyntax statement, State state)
    {
        Read(statement.Expression, state);
        var result = new List<State>();
        var hasDefault = false;
        var errorPair = FindErrorPair(statement.Expression, state);
        var hasNullCase = statement.Sections.SelectMany(s => s.Labels)
            .OfType<CaseSwitchLabelSyntax>().Any(l => l.Value.IsKind(SyntaxKind.NullLiteralExpression));
        var hasNonNullCase = statement.Sections.SelectMany(s => s.Labels)
            .OfType<CasePatternSwitchLabelSyntax>().Any(label =>
                label.WhenClause is null && label.Pattern is DeclarationPatternSyntax declaration &&
                SymbolEqualityComparer.Default.Equals(_model.GetTypeInfo(declaration.Type).Type, _appError));
        foreach (var section in statement.Sections)
        {
            if (section.Labels.Any(x => x is DefaultSwitchLabelSyntax)) hasDefault = true;
            var path = state.Copy();
            if (errorPair is not null)
            {
                var sectionHasNull = section.Labels.OfType<CaseSwitchLabelSyntax>()
                    .Any(label => label.Value.IsKind(SyntaxKind.NullLiteralExpression));
                if (section.Labels.Count == 1 && sectionHasNull)
                    path.Pairs[errorPair] = Outcome.Success;
                else if (section.Labels.Count == 1 && section.Labels[0] is DefaultSwitchLabelSyntax &&
                         hasNonNullCase && !hasNullCase)
                    path.Pairs[errorPair] = Outcome.Success;
                else if (!sectionHasNull &&
                         (section.Labels.All(label => label is not DefaultSwitchLabelSyntax) || hasNullCase))
                    path.Pairs[errorPair] = Outcome.Failure;
            }
            foreach (var label in section.Labels.OfType<CasePatternSwitchLabelSyntax>())
                if (label.WhenClause is not null) Read(label.WhenClause.Condition, path);
            var exits = RunStatements(section.Statements, One(path));
            foreach (var exit in exits)
            {
                exit.Break = false;
                result.Add(exit);
            }
        }
        if (!hasDefault) result.Add(state);
        return result;
    }

    private Pair? FindErrorPair(ExpressionSyntax expression, State state)
    {
        var symbol = Symbol(expression);
        return symbol is null ? null : state.Pairs.Keys.FirstOrDefault(pair =>
            SymbolEqualityComparer.Default.Equals(pair.Error, symbol));
    }

    private static bool IsNullPattern(PatternSyntax pattern) =>
        pattern is ConstantPatternSyntax constant && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression);

    private (List<State> yes, List<State> no) Condition(ExpressionSyntax expression, State state)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
            return Condition(parenthesized.Expression, state);
        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            var (yes, no) = Condition(unary.Operand, state);
            return (no, yes);
        }
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression))
        {
            var (leftYes, leftNo) = Condition(binary.Left, state);
            var yes = new List<State>();
            var no = leftNo;
            foreach (var path in leftYes)
            {
                var (rightYes, rightNo) = Condition(binary.Right, path);
                yes.AddRange(rightYes);
                no.AddRange(rightNo);
            }
            return (yes, no);
        }
        if (expression is BinaryExpressionSyntax or && or.IsKind(SyntaxKind.LogicalOrExpression))
        {
            var (leftYes, leftNo) = Condition(or.Left, state);
            var yes = leftYes;
            var no = new List<State>();
            foreach (var path in leftNo)
            {
                var (rightYes, rightNo) = Condition(or.Right, path);
                yes.AddRange(rightYes);
                no.AddRange(rightNo);
            }
            return (yes, no);
        }

        if (expression is BinaryExpressionSyntax typeTest && typeTest.IsKind(SyntaxKind.IsExpression))
        {
            Read(expression, state);
            var yes = state.Copy();
            var no = state.Copy();
            var conditionalAccess = typeTest.Left as ConditionalAccessExpressionSyntax;
            var subject = Symbol(conditionalAccess?.Expression ?? typeTest.Left);
            foreach (var pair in state.Pairs.Keys)
            {
                if (!SymbolEqualityComparer.Default.Equals(pair.Error, subject)) continue;
                yes.Pairs[pair] = Outcome.Failure;
                no.Pairs[pair] = conditionalAccess is null ? Outcome.Success : Outcome.Unknown;
            }
            return (One(yes), One(no));
        }

        Read(expression, state);
        var trueState = state.Copy();
        var falseState = state.Copy();
        if (TryNullTest(expression, out var symbol, out var trueWhenNull))
        {
            foreach (var pair in state.Pairs.Keys)
            {
                if (!SymbolEqualityComparer.Default.Equals(pair.Error, symbol)) continue;
                trueState.Pairs[pair] = trueWhenNull ? Outcome.Success : Outcome.Failure;
                falseState.Pairs[pair] = trueWhenNull ? Outcome.Failure : Outcome.Success;
            }
        }
        else if (expression is IsPatternExpressionSyntax patterned)
        {
            var subject = patterned.Expression is ConditionalAccessExpressionSyntax conditionalAccess
                ? Symbol(conditionalAccess.Expression)
                : Symbol(patterned.Expression);
            var pattern = patterned.Pattern;
            var negated = pattern is UnaryPatternSyntax unaryPattern;
            if (pattern is UnaryPatternSyntax notPattern) pattern = notPattern.Pattern;
            var knownNonNull = pattern is DeclarationPatternSyntax or TypePatternSyntax ||
                pattern is RecursivePatternSyntax ||
                patterned.Expression is ConditionalAccessExpressionSyntax && pattern is ConstantPatternSyntax;
            if (knownNonNull && subject is not null)
            {
                foreach (var pair in state.Pairs.Keys)
                {
                    if (!SymbolEqualityComparer.Default.Equals(pair.Error, subject)) continue;
                    var falseMeansNull = pattern is DeclarationPatternSyntax or TypePatternSyntax ||
                        pattern is RecursivePatternSyntax recursive && recursive.PropertyPatternClause is null;
                    var opposite = falseMeansNull ? Outcome.Success : Outcome.Unknown;
                    trueState.Pairs[pair] = negated ? opposite : Outcome.Failure;
                    falseState.Pairs[pair] = negated ? Outcome.Failure : opposite;
                }
            }
        }
        else if (expression is ConditionalAccessExpressionSyntax access &&
                 access.WhenNotNull.DescendantNodesAndSelf().OfType<IsPatternExpressionSyntax>()
                     .Any(pattern => pattern.Pattern is ConstantPatternSyntax))
        {
            var subject = Symbol(access.Expression);
            foreach (var pair in state.Pairs.Keys)
                if (SymbolEqualityComparer.Default.Equals(pair.Error, subject))
                    trueState.Pairs[pair] = Outcome.Failure;
        }
        return (One(trueState), One(falseState));
    }

    private bool TryNullTest(ExpressionSyntax expression, out ISymbol? symbol, out bool trueWhenNull)
    {
        symbol = null;
        trueWhenNull = false;
        if (expression is IsPatternExpressionSyntax pattern)
        {
            symbol = Symbol(pattern.Expression);
            if (pattern.Pattern is ConstantPatternSyntax constant && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            { trueWhenNull = true; return symbol is not null; }
            if (pattern.Pattern is UnaryPatternSyntax unary && unary.Pattern is ConstantPatternSyntax negative &&
                negative.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            { trueWhenNull = false; return symbol is not null; }
        }
        if (expression is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)))
        {
            var candidate = binary.Left.IsKind(SyntaxKind.NullLiteralExpression) ? binary.Right :
                binary.Right.IsKind(SyntaxKind.NullLiteralExpression) ? binary.Left : null;
            if (candidate is not null)
            {
                symbol = Symbol(candidate);
                trueWhenNull = binary.IsKind(SyntaxKind.EqualsExpression);
                return symbol is not null;
            }
        }
        return false;
    }

    private void Read(SyntaxNode expression, State state)
    {
        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (ReferenceEquals(invocation, expression) ||
                IsInsideNestedFunction(invocation, expression) ||
                invocation.Ancestors().OfType<AwaitExpressionSyntax>().Any() ||
                IsTaskWrapper(expression))
                continue;
            if (IsResultOrTask(_model.GetTypeInfo(invocation).Type))
                Report(ResultSafetyAnalyzer.IgnoredResult, invocation.GetLocation());
        }
        if (state.Pairs.Count == 0) return;
        foreach (var member in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var receiver = Symbol(member.Expression);
            if (receiver is not null && state.Pairs.Any(entry =>
                SymbolEqualityComparer.Default.Equals(entry.Key.Error, receiver) &&
                entry.Value is Outcome.Unknown or Outcome.Success))
                Report(ResultSafetyAnalyzer.UnsafeFlow, member.GetLocation());
        }
        foreach (var assignment in expression.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            var target = Symbol(assignment.Left);
            if (target is not null && state.Pairs.Keys.Any(pair =>
                SymbolEqualityComparer.Default.Equals(pair.Error, target) ||
                SymbolEqualityComparer.Default.Equals(pair.Value, target)))
                Report(ResultSafetyAnalyzer.UnsafeFlow, assignment.GetLocation());
        }
        foreach (var argument in expression.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
        {
            if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                var target = Symbol(argument.Expression);
                if (target is not null && state.Pairs.Keys.Any(pair =>
                    SymbolEqualityComparer.Default.Equals(pair.Value, target) ||
                    SymbolEqualityComparer.Default.Equals(pair.Error, target)))
                    Report(ResultSafetyAnalyzer.UnsafeFlow, argument.GetLocation());
            }
        }
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Ancestors().OfType<InvocationExpressionSyntax>().Any(i =>
                i.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "nameof"))
                continue;
            var symbol = Symbol(identifier);
            if (symbol is null) continue;
            foreach (var entry in state.Pairs)
            {
                if (entry.Key.Value is not null &&
                    SymbolEqualityComparer.Default.Equals(entry.Key.Value, symbol) &&
                    entry.Value is Outcome.Unknown or Outcome.Failure)
                {
                    _unsafePairs.Add(entry.Key);
                    Report(ResultSafetyAnalyzer.UnsafeValue, identifier.GetLocation(), symbol.Name);
                }
            }
        }
    }

    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode root)
    {
        if (root is LambdaExpressionSyntax or LocalFunctionStatementSyntax or AnonymousMethodExpressionSyntax)
            return true;
        for (var parent = node.Parent; parent is not null && parent != root; parent = parent.Parent)
            if (parent is LambdaExpressionSyntax or LocalFunctionStatementSyntax or AnonymousMethodExpressionSyntax)
                return true;
        return false;
    }

    private bool IsTaskWrapper(SyntaxNode root)
    {
        if (root is not InvocationExpressionSyntax invocation) return false;
        var method = _model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return method is not null && method.Name == "FromResult" &&
            method.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
            (method.ContainingType.Name == "Task" || method.ContainingType.Name == "ValueTask") &&
            IsResultOrTask(method.ReturnType);
    }

    private void CheckReturn(State state, ReturnStatementSyntax returned, ExpressionSyntax? expression)
    {
        foreach (var pending in state.Pending.Values.Distinct())
            Report(ResultSafetyAnalyzer.IgnoredResult, pending);
        foreach (var entry in state.Pairs)
        {
            if (entry.Value == Outcome.Unknown && !_unsafePairs.Contains(entry.Key))
                Report(ResultSafetyAnalyzer.IgnoredResult, entry.Key.Location);
            else if (entry.Value == Outcome.Failure && !PropagatesFailure(expression, entry.Key.Error) &&
                     !_unsafePairs.Contains(entry.Key))
                Report(ResultSafetyAnalyzer.UnsafeFlow, returned.GetLocation());
        }
    }

    private bool PropagatesFailure(ExpressionSyntax? expression, ISymbol error)
    {
        if (expression is null) return false;
        if (SymbolEqualityComparer.Default.Equals(Symbol(expression), error)) return true;
        var expressionType = _model.GetTypeInfo(expression).Type;
        if (SymbolEqualityComparer.Default.Equals(expressionType, _appError) &&
            expressionType?.NullableAnnotation != NullableAnnotation.Annotated)
            return true;
        if (expression is InvocationExpressionSyntax invocation &&
            _model.GetTypeInfo(invocation).Type is INamedTypeSymbol type &&
            SymbolEqualityComparer.Default.Equals(type, _appError))
            return true;
        if (expression is SwitchExpressionSyntax switched &&
            switched.Arms.Any(arm => arm.Pattern is DiscardPatternSyntax) &&
            switched.Arms.All(arm => PropagatesFailure(arm.Expression, error)))
            return true;
        return false;
    }

    private void ReportUnfinished(State state, Location location)
    {
        foreach (var pending in state.Pending.Values.Distinct())
            Report(ResultSafetyAnalyzer.IgnoredResult, pending);
        foreach (var entry in state.Pairs)
        {
            if (entry.Value == Outcome.Unknown && !_unsafePairs.Contains(entry.Key))
                Report(ResultSafetyAnalyzer.IgnoredResult, entry.Key.Location);
            else if (entry.Value == Outcome.Failure && !_unsafePairs.Contains(entry.Key))
                Report(ResultSafetyAnalyzer.UnsafeFlow, location);
        }
    }

    private void ReportUnobservedAtThrow(State state)
    {
        foreach (var pending in state.Pending.Values.Distinct())
            Report(ResultSafetyAnalyzer.IgnoredResult, pending);
        foreach (var entry in state.Pairs)
            if (entry.Value == Outcome.Unknown && !_unsafePairs.Contains(entry.Key))
                Report(ResultSafetyAnalyzer.IgnoredResult, entry.Key.Location);
    }

    private bool ContainsResultWork(StatementSyntax statement) =>
        statement.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>()
            .Any(a => _model.GetOperation(a) is IDeconstructionAssignmentOperation d && IsResult(d.Value.Type)) ||
        statement.DescendantNodesAndSelf().OfType<ExpressionSyntax>()
            .Any(e => IsResultOrTask(_model.GetTypeInfo(e).Type));

    private bool IsInvalidFallback(Pair pair, ExpressionSyntax expression)
    {
        if (expression is PostfixUnaryExpressionSyntax postfix &&
            postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
            IsDefaultLike(postfix.Operand))
            return true;
        var valueType = pair.SuccessType;
        if (valueType is null || !valueType.IsReferenceType ||
            valueType.NullableAnnotation == NullableAnnotation.Annotated)
            return false;
        return IsDefaultLike(expression);
    }

    private static bool IsDefaultLike(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) ||
                                            literal.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
        DefaultExpressionSyntax => true,
        PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
            IsDefaultLike(postfix.Operand),
        ParenthesizedExpressionSyntax parenthesized => IsDefaultLike(parenthesized.Expression),
        _ => false,
    };

    private bool IsResultOrTask(ITypeSymbol? type)
    {
        if (IsResult(type)) return true;
        if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1 &&
            named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
            (named.Name == "Task" || named.Name == "ValueTask"))
            return IsResult(named.TypeArguments[0]);
        return false;
    }

    private bool IsResult(ITypeSymbol? type) => type is INamedTypeSymbol named &&
        (SymbolEqualityComparer.Default.Equals(named, _result) ||
         SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, _genericResult));

    private ISymbol? Symbol(ExpressionSyntax expression) =>
        _model.GetSymbolInfo(expression).Symbol;

    private ISymbol? OutcomeSource(ExpressionSyntax expression) => expression switch
    {
        AwaitExpressionSyntax awaited => OutcomeSource(awaited.Expression),
        ParenthesizedExpressionSyntax parenthesized => OutcomeSource(parenthesized.Expression),
        _ => Symbol(expression),
    };

    private static void ClearPending(State state, ISymbol source)
    {
        if (!state.Pending.TryGetValue(source, out var obligation)) return;
        foreach (var symbol in state.Pending.Where(x => ReferenceEquals(x.Value, obligation)).Select(x => x.Key).ToArray())
            state.Pending.Remove(symbol);
    }

    private static List<State> One(State state) => new() { state };

    private static List<State> MergeEquivalent(List<State> states)
    {
        var merged = new List<State>();
        foreach (var state in states)
        {
            if (merged.Any(existing => SameState(existing, state))) continue;
            merged.Add(state);
        }
        return merged;
    }

    private static bool SameState(State left, State right)
    {
        if (left.Break != right.Break || left.Pairs.Count != right.Pairs.Count ||
            left.Pending.Count != right.Pending.Count) return false;
        foreach (var entry in left.Pairs)
            if (!right.Pairs.TryGetValue(entry.Key, out var outcome) || outcome != entry.Value)
                return false;
        foreach (var entry in left.Pending)
            if (!right.Pending.TryGetValue(entry.Key, out var location) ||
                !ReferenceEquals(location, entry.Value))
                return false;
        return true;
    }

    private void Report(DiagnosticDescriptor descriptor, Location location, params object[] args)
    {
        var key = descriptor.Id + ":" + location.SourceSpan.Start + ":" + location.SourceSpan.Length;
        if (_reported.Add(key))
            _context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
    }
}
