using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the names that a literal of a collection makes, for WrittenAfterItsOwner.
internal sealed partial class LoopStateEvaluator
{
    // Records that each element of a literal is a second name of the value at its position, as after a[1] = d. An
    // object that the store does not know has no other record of the two names. A sure assignment took away the
    // links that the name had before: see NotesALink. Only a local, or a field with no owner, gives its elements the
    // key of the name. Thus, a field of a holder or an element makes no link here.
    private void NotesTheLinksOfALiteral(NameKey name, ExpressionSyntax value)
    {
        ExpressionSyntax made = Unwrapped(value);

        if (name is not { Owner: null, Index: -1 }
            || this.LiteralElements(
                    made: made,
                    type: made is CollectionExpressionSyntax ? this.region.TypeOf(made) : null)
                is not { Count: > 0 } elements)
        {
            return;
        }

        for (int index = 0; index < elements.Count; index++)
        {
            this.NotesALink(
                name: new NameKey(of: name.Of, owner: null, index: index),
                value: elements[index],
                replaces: false);
        }
    }

    // A name that gets a new collection does not hold the collection that it had before. Thus, a local that got an
    // element of that collection is no element of the name: see HoldsOneOf. A change through the local does not go to
    // the new collection. Where the new collection gets the local, Gets records that.
    private void ForgetsTheElementsOf(NameKey name, ExpressionSyntax value)
    {
        if (this.Frame.ElementOf.Count == 0
            || Unwrapped(value)
                is not (CollectionExpressionSyntax
                or ArrayCreationExpressionSyntax
                or ImplicitArrayCreationExpressionSyntax
                or BaseObjectCreationExpressionSyntax))
        {
            return;
        }

        foreach (KeyValuePair<ISymbol, ExpressionSyntax> one in this.Frame.ElementOf.ToArray())
        {
            if (this.NameKeyOf(one.Value) is { } collection && collection.IsTheSameAs(name))
            {
                this.Frame.ElementOf.Remove(one.Key);
            }
        }
    }
}
