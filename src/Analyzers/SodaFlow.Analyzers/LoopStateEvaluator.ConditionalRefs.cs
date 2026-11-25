using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a ref to one of two locations, where the walk does not know which.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The conditional ref that is the location of a name with ref, when
    ///     the walk can read it again. The condition is not a constant. Null for
    ///     each other expression.
    /// </summary>
    /// <remarks>
    ///     The walk does not know which of the two locations the ref selects.
    ///     Thus, a write through the name goes to each location, and replaces
    ///     the value of no location.
    /// </remarks>
    /// <param name="written">The expression of the name.</param>
    public ExpressionSyntax? TheConditionalLocationOf(ExpressionSyntax written) =>
        this.TheLocationIn(locations: this.Frame.ConditionalLocationOf, written: written);

    // A local that gets a conditional ref of two locations, on a condition that is not a constant,
    // is a name of the two. Each other ref takes that record away.
    private void NotesAConditionalLocation(ISymbol local, ExpressionSyntax location, bool replaces)
    {
        if (replaces && this.IsAConditionalRefOfTwoLocations(location))
        {
            this.Frame.ConditionalLocationOf[local] = location;
        }
        else
        {
            this.Frame.ConditionalLocationOf.Remove(local);
        }
    }

    // True for a location that a local with ref can name, and that the walk can read again.
    private bool IsALocationOfARefLocal(ExpressionSyntax location) =>
        this.IsALocationThatTheWalkCanReadAgain(location)
        || this.IsAStaticField(location)
        || this.IsARefIndexerOfALocal(location)
        || this.IsAFieldOfAnElementOfAStaticArray(location);

    // True for a conditional ref whose two branches are locations of a local with ref. A branch can
    // be such a conditional ref also.
    private bool IsAConditionalRefOfTwoLocations(ExpressionSyntax location) =>
        Unwrapped(location) is ConditionalExpressionSyntax
        {
            WhenTrue: RefExpressionSyntax whenTrue, WhenFalse: RefExpressionSyntax whenFalse
        }
        && new[] { this.SelectedLocation(whenTrue.Expression), this.SelectedLocation(whenFalse.Expression) }
            .All(branch => this.IsALocationOfARefLocal(branch) || this.IsAConditionalRefOfTwoLocations(branch));

    // The locations that a returned ref can be. A conditional ref on a constant is the location
    // that the constant selects. On each other condition, it can be each of its two locations.
    private IEnumerable<ExpressionSyntax> LocationsOfAConditionalRef(ExpressionSyntax returned)
    {
        ExpressionSyntax selected = this.SelectedLocation(returned);

        return Unwrapped(selected) is ConditionalExpressionSyntax
        {
            WhenTrue: RefExpressionSyntax whenTrue, WhenFalse: RefExpressionSyntax whenFalse
        }
            ? this.LocationsOfAConditionalRef(whenTrue.Expression)
                .Concat(this.LocationsOfAConditionalRef(whenFalse.Expression))
            : [selected];
    }
}
