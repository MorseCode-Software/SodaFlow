using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for two methods of Delegate. One makes one delegate of the functions of others. The other
// gives those functions one by one.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of a call of <c>Delegate.Combine</c> or of
    ///     <c>GetInvocationList</c>. Null for each other call.
    /// </summary>
    /// <remarks>
    ///     <c>Combine</c> makes a delegate that runs the functions of each
    ///     delegate that it gets, as the plus of two delegates does. An array of
    ///     delegates gives the functions of its elements. Each element of the
    ///     array of <c>GetInvocationList</c> is one function of its owner.
    /// </remarks>
    private LoopState? StateOfADelegateCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.ContainingType?.SpecialType
            is not (SpecialType.System_Delegate or SpecialType.System_MulticastDelegate))
        {
            return null;
        }

        return method switch
        {
            { Name: "Combine", IsStatic: true } => invocation.ArgumentList.Arguments.Aggregate(
                seed: LoopState.None,
                func: (all, argument) =>
                    all.Join(
                        this.region.TypeOf(argument.Expression) is IArrayTypeSymbol
                            ? this.Eval(argument.Expression).AsOneOfItsElements
                            : this.Eval(argument.Expression))),
            { Name: "GetInvocationList", IsStatic: false } =>
                ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method) is { } owner
                    ? this.Eval(owner).AsContainer
                    : null,
            _ => null
        };
    }

    /// <summary>
    ///     The functions of a delegate in the order of its invocation list. Each
    ///     one is a lambda, an anonymous method, a name of a method, or a
    ///     creation of a delegate from one of these. A local with one value
    ///     gives the functions of that value. <c>Delegate.Combine</c> and the
    ///     plus of two delegates give the functions of each delegate in order.
    ///     Null for each other delegate.
    /// </summary>
    private IReadOnlyList<ExpressionSyntax>? FunctionsInOrder(ExpressionSyntax written)
    {
        ExpressionSyntax delegated = Unwrapped(written);

        while (delegated is IdentifierNameSyntax name && this.region.SymbolOf(name) is ILocalSymbol local)
        {
            if (this.OnlyValueOf(local) is not { } value)
            {
                return null;
            }

            delegated = Unwrapped(value);
        }

        switch (delegated)
        {
            case AnonymousFunctionExpressionSyntax:
            case ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } only }
                when Unwrapped(only[0].Expression) is AnonymousFunctionExpressionSyntax
                     || this.region.SymbolOf(only[0].Expression) is IMethodSymbol:
            case SimpleNameSyntax or MemberAccessExpressionSyntax when this.region.SymbolOf(delegated) is IMethodSymbol:
                return [delegated];
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } plus
                when this.region.TypeOf(plus) is { TypeKind: TypeKind.Delegate }:
                return this.FunctionsInOrder(plus.Left) is { } left && this.FunctionsInOrder(plus.Right) is { } right
                    ? [.. left, .. right]
                    : null;
            case InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Combine" }
                } call
                when this.region.SymbolOf(call) is IMethodSymbol
                     {
                         IsStatic: true, ContainingType.SpecialType: SpecialType.System_Delegate
                     } combine
                     && combine.Parameters.All(static parameter => parameter.Type is not IArrayTypeSymbol):
            {
                List<ExpressionSyntax> all = [];

                foreach (ArgumentSyntax argument in call.ArgumentList.Arguments)
                {
                    if (this.FunctionsInOrder(argument.Expression) is not { } each)
                    {
                        return null;
                    }

                    all.AddRange(each);
                }

                return all;
            }
            default:
                return null;
        }
    }
}
