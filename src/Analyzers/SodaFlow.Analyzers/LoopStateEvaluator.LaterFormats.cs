using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a FormattableString that the code formats after it makes the string.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The calls of <c>ToString</c> that a format of a kept interpolated
    ///     string makes on its values. The string is the only value of a local
    ///     of type <c>FormattableString</c> or <c>IFormattable</c>. The format is
    ///     a <c>ToString</c> of that local. It can also be a call of
    ///     <c>Invariant</c> that gets the local, and the same holds for
    ///     <c>CurrentCulture</c>. Null for each other call.
    /// </summary>
    /// <remarks>
    ///     The walk reads each value as it is at the format. A different value in
    ///     a local of the string can come between the two, and the walk does not
    ///     see it.
    /// </remarks>
    private Followed? FollowLaterFormat(InvocationExpressionSyntax invocation, ISymbol method)
    {
        if (method.Name is not ("ToString" or "Invariant" or "CurrentCulture")
            || method.ContainingType is not
            {
                Name: "FormattableString" or "IFormattable",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            }
            || (method.IsStatic
                    ? invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                    : (invocation.Expression as MemberAccessExpressionSyntax)?.Expression)
                is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not ILocalSymbol local
            || this.OnlyValueOf(local) is not { } value
            || Unwrapped(value) is not InterpolatedStringExpressionSyntax kept)
        {
            return null;
        }

        return kept.Contents
            .OfType<InterpolationSyntax>()
            .Aggregate(
                seed: (Followed?)null,
                func: (all, part) =>
                    Both(first: all, second: this.FollowInterpolation(interpolation: part, formatsNow: true)));
    }
}
