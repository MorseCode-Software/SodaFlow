using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that finds a call of a virtual method whose body the walk cannot know.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when a call of the method on an object that the walk did not see
    ///     made can run an override that the compilation declares. The walk then
    ///     does not know which body runs, as for a call through an interface.
    /// </summary>
    /// <remarks>
    ///     A call on <c>base</c> runs the method that it names. A call with no
    ///     receiver keeps the method that it names: the walk does not know the
    ///     type of the object there. A method with no body to read keeps the
    ///     rules that the table of the platform gives it.
    /// </remarks>
    private bool CanRunAnOverride(ExpressionSyntax? receiver, IMethodSymbol definition) =>
        receiver is not (null or BaseExpressionSyntax)
        && definition is { IsVirtual: true } or { IsOverride: true, IsSealed: false }
        && BodyOf(definition) != null
        && this.region.Symbols.Overrides.CanRunFor(method: definition, receiver: this.region.TypeOf(receiver));
}
