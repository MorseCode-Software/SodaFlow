using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that the escape check of the walk asks. It reads a value that the code
// put into a local or gave a parameter.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     False when the walk read the expression and gave it a state that is
    ///     not pending. True for an expression that the walk did not read. Then,
    ///     only the type of the expression can tell.
    /// </summary>
    /// <remarks>
    ///     The check does not read the expression itself. The first read of a
    ///     node keeps its state for each read after it. Thus, a read out of the
    ///     order of the code gives the node an incorrect state.
    /// </remarks>
    public bool CanBePending(ExpressionSyntax expression) =>
        !this.memo.TryGetValue(key: expression, value: out LoopState? known) || known.Pending;

    /// <summary>
    ///     The arguments that the call of this code gives a parameter, with the
    ///     evaluator of the caller. A <c>params</c> parameter can get more than
    ///     one. Null when this evaluator does not read a call of the method of
    ///     the parameter.
    /// </summary>
    public (LoopStateEvaluator Caller, IReadOnlyList<ExpressionSyntax> Arguments)? ArgumentsInTheCaller(
        IParameterSymbol parameter) =>
        this.call is { Caller: var caller, Site: var site }
        && caller.region.SymbolOf(site) is IMethodSymbol method
        && SymbolEqualityComparer.Default.Equals(
            x: (method.ReducedFrom ?? method).OriginalDefinition,
            y: parameter.ContainingSymbol.OriginalDefinition)
            ? (caller, ArgumentsFor(method: method, site: site, parameter: parameter))
            : null;

    /// <summary>
    ///     The arguments that a call gives a parameter of its method. The
    ///     receiver of a call of an extension method is its first argument.
    /// </summary>
    public static IReadOnlyList<ExpressionSyntax> ArgumentsFor(
        IMethodSymbol method,
        SyntaxNode site,
        IParameterSymbol parameter)
    {
        if (method.ReducedFrom != null && parameter.Ordinal == 0)
        {
            return site is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: var receiver }
            }
                ? [receiver]
                : [];
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments =
            site switch
            {
                InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments,
                BaseObjectCreationExpressionSyntax { ArgumentList: { } list } => list.Arguments,
                _ => default
            };

        return
        [
            .. arguments
                .Where((argument, position) =>
                    ParameterFor(method: method, argument: argument, position: position)?.Ordinal
                    == parameter.Ordinal)
                .Select(static argument => argument.Expression)
        ];
    }
}
