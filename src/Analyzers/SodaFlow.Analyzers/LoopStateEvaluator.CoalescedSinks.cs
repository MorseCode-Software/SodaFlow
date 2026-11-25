using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a sink whose coalesce function always runs.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when the sink fires only what its coalesce function returns.
    ///     The sink is one object that a call of the block made. Each send of
    ///     the read before goes into it in the block. Two or more of those
    ///     sends always run. The function is a lambda or a method of this
    ///     compilation, which the walk can read. The creation of the sink then
    ///     has the value that the function returns. Thus, a send adds nothing
    ///     to it.
    /// </summary>
    /// <remarks>
    ///     The walk cannot know what a function that it cannot read returns.
    ///     Thus, for such a function, each send gives its value to the sink, as
    ///     for a sink with no coalesce function.
    /// </remarks>
    /// <param name="sink">The receiver of the send.</param>
    public bool FiresOnlyWhatItsFunctionReturns(ExpressionSyntax sink)
    {
        if (!this.region.Sent.HasCoalescing)
        {
            return false;
        }

        ImmutableArray<MadeObject> sinks = this.region.Objects.Now(this.Eval(sink).Objects);

        return sinks.Length == 1
               && this.region.Objects.CreationOfASink(sinks[0]) is InvocationExpressionSyntax creation
               && this.region.SentBefore.AlwaysCoalesces(creation)
               && this.region.SymbolOf(creation) is IMethodSymbol method
               && creation.ArgumentList.Arguments
                   .Where((argument, position) =>
                       ParameterFor(method: method, argument: argument, position: position) is { Name: "coalesce" })
                   .Any(argument =>
                       this.region.SymbolOf(argument.Expression)
                           is IMethodSymbol { DeclaringSyntaxReferences.Length: > 0 });
    }
}
