using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for two parameters with ref or in that a call gives one location.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Gives a write through a parameter with ref to each other parameter
    ///     whose argument is the same location. Each such parameter is a name of
    ///     that location.
    /// </summary>
    /// <param name="parameter">The parameter that the code writes.</param>
    /// <param name="state">The state of the location after the write.</param>
    /// <param name="replaces">
    ///     True when the write always runs. Then, the location has the new state
    ///     only. Otherwise, it has the state before it also.
    /// </param>
    private void WritesTheOtherRefsOf(ISymbol parameter, LoopState state, bool replaces)
    {
        // Only a call with two or more locations can give one location two times.
        if (this.call is not { Locations.Count: > 1 } || parameter is not IParameterSymbol { RefKind: RefKind.Ref })
        {
            return;
        }

        foreach (ISymbol other in this.OtherRefsOf(parameter))
        {
            if (replaces)
            {
                this.locals[other] = state;
                this.assigned.Add(other);
                this.Frame.PutsBeforeAnAssignment[other] = this.Frame.PutsIntoLocations.Count;
            }
            else
            {
                this.locals[other] =
                    this.locals.TryGetValue(key: other, value: out LoopState? existing) ? existing.Join(state) : state;
            }
        }
    }

    // The other parameters with ref or in of this code whose arguments in the call are the location of the
    // parameter. A parameter with out is not one. The walk of a Deconstruct method gives each out parameter
    // the value of the deconstruction as its argument: see FollowDeconstruct.
    private List<ISymbol> OtherRefsOf(ISymbol parameter)
    {
        List<ISymbol> others = [];

        if (this.call == null
            || !this.call.Locations.TryGetValue(
                key: parameter,
                value: out (LoopStateEvaluator Reader, ExpressionSyntax Location) of))
        {
            return others;
        }

        foreach (KeyValuePair<ISymbol, (LoopStateEvaluator Reader, ExpressionSyntax Location)> pair in
                 this.call.Locations)
        {
            if (pair.Key is IParameterSymbol { RefKind: not RefKind.Out }
                && !SymbolEqualityComparer.Default.Equals(x: pair.Key, y: parameter)
                && ReferenceEquals(objA: pair.Value.Reader, objB: of.Reader)
                && of.Reader.IsTheSameLocation(one: pair.Value.Location, other: of.Location))
            {
                others.Add(pair.Key);
            }
        }

        return others;
    }

    // True for two expressions of one location in one function. The two have the same text, which
    // names the same local, field, or element there. Two names of a static field can have a
    // different text.
    private bool IsTheSameLocation(ExpressionSyntax one, ExpressionSyntax other) =>
        SyntaxFactory.AreEquivalent(oldNode: Unwrapped(one), newNode: Unwrapped(other))
        || (this.IsAStaticField(one)
            && this.IsAStaticField(other)
            && SymbolEqualityComparer.Default.Equals(
                x: this.region.SymbolOf(Unwrapped(one)),
                y: this.region.SymbolOf(Unwrapped(other))));
}
