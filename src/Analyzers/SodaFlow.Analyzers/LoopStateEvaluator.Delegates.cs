using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the methods of Delegate that make one delegate of the functions of others, or that give
// those functions one by one.
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
}
