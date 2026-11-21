using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a method group that names a method of the object of this code.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when a method group names a method of the object that this code
    ///     is in. The group has no owner, or its owner is this or base.
    /// </summary>
    /// <remarks>
    ///     A callee can run the delegate in the call. A map or a
    ///     <c>Lazy</c> can keep the delegate and run it after a different
    ///     method changes a member that the method reads. Thus, the walk of the
    ///     method gets the members of this, as a call of the method on this
    ///     does.
    /// </remarks>
    /// <param name="expression">The method group.</param>
    /// <param name="group">The method that the group names.</param>
    private static bool NamesAMethodOfThis(ExpressionSyntax expression, IMethodSymbol group) =>
        !(group.ReducedFrom ?? group).IsStatic
        && Unwrapped(expression)
            is SimpleNameSyntax
            or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax };
}
