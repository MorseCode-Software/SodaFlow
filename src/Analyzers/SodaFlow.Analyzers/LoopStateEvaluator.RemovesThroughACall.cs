using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a remove of a handler that a call of a function of this code runs.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of a delegate location after a call whose function removes
    ///     a handler, as <see cref="AfterTheLastRemove" /> gives it for a remove
    ///     in the code. Null when the walk is not sure that the call takes away
    ///     the last occurrence of the lambda.
    /// </summary>
    /// <remarks>
    ///     The function is a method of this compilation that the call runs, a
    ///     lambda in a local, or a local function. Its first statement is the
    ///     remove, and it names the handler nowhere else. Thus, no code of it
    ///     can change a name of the remove before the remove. A parameter of the
    ///     function gets its argument, and each other name is a local of the
    ///     code of the call. The walk asks only for a call that each run of the
    ///     code runs.
    /// </remarks>
    /// <param name="call">The call.</param>
    /// <returns>
    ///     The name of the location, or of the owner of the event, in the code
    ///     of the call. The event, or null for a local. The state.
    /// </returns>
    public (ExpressionSyntax Target, ISymbol? Member, LoopState State)? AfterARemoveThroughACall(
        InvocationExpressionSyntax call)
    {
        if (this.quiet
            || FunctionOf(call) is not { } code
            || this.RemoveOfACall(call) is not (var handler, var target, var location, var ran)
            || this.TheLambdaOfTheLastRemove(code: code, at: call, handler: handler, location: location, ran: ran)
                is not { } lambda)
        {
            return null;
        }

        LoopState now = this.EvalAgain(target);

        return (target,
            location.Member,
            (location.Member != null ? (now.Members ?? Construction.Empty).StateOf(location.Member) : now)
            .WithoutFunctionsOf(lambda));
    }

    // The remove that is the first statement of the function of a call. The result has the handler,
    // the target, and the location, in the names of the code of the call. The uses of the names in
    // the last part are uses of the remove. Null for each other call.
    private (ExpressionSyntax Handler, ExpressionSyntax Target, (ISymbol Variable, ISymbol? Member) Location,
        IReadOnlyCollection<SyntaxNode> Ran)? RemoveOfACall(InvocationExpressionSyntax call)
    {
        (SyntaxNode? body, IMethodSymbol? method) =
            this.region.SymbolOf(call) switch
            {
                // A lambda in a local that gets no second value.
                IMethodSymbol { MethodKind: MethodKind.DelegateInvoke }
                    when call.Expression is IdentifierNameSyntax name
                         && this.region.SymbolOf(name) is ILocalSymbol delegateLocal
                         && delegateLocal.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                             is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
                         && Unwrapped(value) is AnonymousFunctionExpressionSyntax lambda
                         && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } scope
                         && !this.GetsASecondValue(local: delegateLocal, scope: scope) => (lambda.Body, null),
                // A method whose call runs its own body, or a local function.
                IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension or MethodKind.LocalFunction,
                    IsVirtual: false,
                    IsOverride: false
                } called => (BodyOf((called.ReducedFrom ?? called).OriginalDefinition), called),
                _ => (null, null)
            };

        if (body == null
            || (body is BlockSyntax { Statements: { Count: > 0 } statements }
                    ? (statements[0] as ExpressionStatementSyntax)?.Expression
                    : body)
                is not AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SubtractAssignmentExpression } remove
            || Unwrapped(remove.Right) is not IdentifierNameSyntax handler
            || NamesInAFunction(function: body, name: handler) != 1
            || this.NamedLocation(remove.Left) is not (var variable, var member)
            || InTheCode(name: handler, symbol: this.region.SymbolOf(handler), call: call, method: method)
                is not { } handlerInTheCode
            || (member != null
                    ? InTheCode(
                        name: OwnerName(remove.Left),
                        symbol: variable,
                        call: call,
                        method: method)
                    : Unwrapped(remove.Left)) is not { } target
            || this.region.SymbolOf(target) is not ILocalSymbol local)
        {
            return null;
        }

        return (handlerInTheCode, target, (local, member), [body, call]);

        static IdentifierNameSyntax OwnerName(ExpressionSyntax location) =>
            (IdentifierNameSyntax)((MemberAccessExpressionSyntax)Unwrapped(location)).Expression;
    }

    // The name in the code of a call that a name in the function of the call has. A parameter of the
    // function has its argument, which must be a name. Each other name is a name that the function
    // reads from the code around it.
    private static ExpressionSyntax? InTheCode(
        ExpressionSyntax name,
        ISymbol? symbol,
        InvocationExpressionSyntax call,
        IMethodSymbol? method)
    {
        if (symbol is not IParameterSymbol parameter)
        {
            return name;
        }

        // The receiver of an extension method is the argument of its first parameter.
        if (method?.ReducedFrom is { } extension
            && SymbolEqualityComparer.Default.Equals(x: extension.OriginalDefinition.Parameters[0], y: parameter))
        {
            return (call.Expression as MemberAccessExpressionSyntax)?.Expression as IdentifierNameSyntax;
        }

        return method == null
            ? null
            : call.ArgumentList.Arguments
                .Where((argument, position) =>
                    SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position) is { } given
                    && SymbolEqualityComparer.Default.Equals(
                        x: method.ReducedFrom is { } reduced
                            ? reduced.OriginalDefinition.Parameters[given.Ordinal + 1]
                            : given.OriginalDefinition,
                        y: parameter))
                .Select(static argument => argument.Expression)
                .FirstOrDefault() as IdentifierNameSyntax;
    }

    // True for a use in one of the nodes.
    private static bool IsInAny(IEnumerable<SyntaxNode> nodes, SyntaxNode use) =>
        nodes.Any(node => node.SyntaxTree == use.SyntaxTree && node.Span.Contains(use.Span));

    // The number of names in a function with the text of the given name.
    private static int NamesInAFunction(SyntaxNode function, SimpleNameSyntax name) =>
        function.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Count(each => each.Identifier.ValueText == name.Identifier.ValueText);
}
