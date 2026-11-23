using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a value of a struct type that a collection expression makes.
internal sealed partial class LoopStateEvaluator
{
    // The state of a value whose type is not a reference type. Such a value is no object of the store.
    // A collection expression of such a type, such as ImmutableArray, holds the objects of its elements.
    private static LoopState OfAStruct(ExpressionSyntax place, LoopState state) =>
        place is CollectionExpressionSyntax ? HoldingItsObjects(state) : state.WithNoObjects;
}
