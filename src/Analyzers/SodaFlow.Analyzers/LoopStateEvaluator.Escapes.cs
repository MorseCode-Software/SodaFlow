using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that the escape check of the walk asks about a value that the code put
// into a local.
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
}
