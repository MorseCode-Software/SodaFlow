using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that tells the walker where code runs on each path.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True for a collection whose positions the store knows, with one
    ///     position or more. A loop over it always has a first turn.
    /// </summary>
    /// <param name="collection">The expression of the collection.</param>
    public bool HasAnElement(ExpressionSyntax collection) =>
        this.StorePositionsOf(collection) is (_, { Length: > 0 });
}
