using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator that ends the walk of a method that calls itself.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     A number for what a call of a method in its own code gives the body.
    ///     Null when the walk does not read that body now, which is the usual
    ///     case.
    /// </summary>
    /// <remarks>
    ///     The walk reads such a call one time. A call after it with the same
    ///     number gives the body the same arguments and members as that read.
    ///     Thus, the walk stops there and does not read the body again. A
    ///     different number shows a new value, and the walk reads the body for
    ///     it, to the depth limit. The first call has no number, because the
    ///     code before it can give the second call new values.
    /// </remarks>
    private int? ShapeOfACallAgain(
        SyntaxNode body,
        IReadOnlyList<Binding> bindings,
        Dictionary<ISymbol, LoopState> members) =>
        this.region.Walks(body)
            ? unchecked(
                Shapes(
                    bindings.Select(static binding =>
                        new KeyValuePair<ISymbol, LoopState>(key: binding.Parameter, value: binding.State)))
                + Shapes(members) * 3)
            : null;
}
