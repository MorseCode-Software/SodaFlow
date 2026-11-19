using System.Linq;
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
    ///     receiver is a call on <c>this</c>, whose type is the type of the code
    ///     of the call. A method with no body to read keeps the rules that the
    ///     table of the platform gives it.
    /// </remarks>
    private bool CanRunAnOverride(ExpressionSyntax? receiver, IMethodSymbol definition, SyntaxNode call) =>
        receiver is not BaseExpressionSyntax
        && definition is { IsVirtual: true } or { IsOverride: true, IsSealed: false }
        && BodyOf(definition) != null
        && this.region.Symbols.Overrides.CanRunFor(
            method: definition,
            receiver: receiver != null ? this.region.TypeOf(receiver) : this.TypeOfTheCodeOf(call));

    /// <summary>
    ///     The method that a call on <c>this</c> runs for each class that the
    ///     object can be, when the walk does not know its class. Each such class
    ///     is the class of the code, or a class that derives from it and
    ///     overrides the method.
    /// </summary>
    private IMethodSymbol[] OnEachClassOfThis(IMethodSymbol definition, SyntaxNode call) =>
        this.TypeOfTheCodeOf(call) is { } type
            ?
            [
                .. this.region.Symbols.Overrides.TypesThatCanRun(method: definition, receiver: type)
                    .Prepend(type)
                    .SelectMany(each => Construction.Of(each.OriginalDefinition).Resolve(definition))
                    .OfType<IMethodSymbol>()
                    .Select(static found => found.OriginalDefinition)
                    .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            ]
            : [];

    // The class that the code of a call is in, which is the type of this there.
    private INamedTypeSymbol? TypeOfTheCodeOf(SyntaxNode call) =>
        this.region.ModelFor(call).GetEnclosingSymbol(call.SpanStart)?.ContainingType;
}
