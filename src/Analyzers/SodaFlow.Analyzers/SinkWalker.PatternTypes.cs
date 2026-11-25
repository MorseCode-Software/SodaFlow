using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for a pattern of a type that the tested value can never have.
internal sealed partial class SinkWalker
{
    // The state that a pattern of the given type gives its variable. A pattern of a type that the value can never have
    // is false. Thus, the variable then gets no state.
    private LoopState MatchedBy(TypeSyntax? type, ExpressionSyntax? value, LoopState state) =>
        !state.IsNone
        && type != null
        && value != null
        && this.region.TypeOf(type) is { } tested
        && this.evaluator.NeverHas(value: value, type: tested)
            ? LoopState.None
            : state;
}
