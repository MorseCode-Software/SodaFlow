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
}
