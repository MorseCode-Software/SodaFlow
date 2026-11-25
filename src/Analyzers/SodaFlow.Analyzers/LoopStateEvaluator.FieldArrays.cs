using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a put into an element of an array in a static field through a different name of the
// array.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Gives a put into an element of the array that a call returns to the
    ///     static field that the callee returns. A read through each name of
    ///     that field then gets the put.
    /// </summary>
    /// <remarks>
    ///     The code of the callee must be only a return of the field: see
    ///     ReturnedBy. Thus, the call gives the array of the field at each run.
    /// </remarks>
    /// <param name="collection">The collection of the element that gets the value.</param>
    /// <param name="state">The state of the value.</param>
    public void PutsThroughAFieldThatACallReturns(ExpressionSyntax collection, LoopState state)
    {
        if (Unwrapped(collection) is InvocationExpressionSyntax call
            && this.ReturnedBy(call) is ({ } returned, not null)
            && this.IsAStaticField(returned))
        {
            this.Absorb(target: returned, state: state);
        }
    }

    // The state of a field with readonly, with the object that the store made for it at a read of the field: see
    // InMember. A read through a name of that object then gets a put into it. Only a constructor can give such a
    // field a different object. A field with no readonly can get a new object that the store does not know.
    private LoopState WithTheObjectsInTheMember(LoopState owner, ISymbol member, LoopState state)
    {
        if (!state.Objects.IsEmpty || member is not IFieldSymbol { IsReadOnly: true })
        {
            return state;
        }

        ImmutableArray<MadeObject> made = ImmutableArray<MadeObject>.Empty;

        foreach (MadeObject one in owner.Objects)
        {
            if (this.region.Objects.InMember(owner: one, member: member) is { } inMember)
            {
                made = made.Add(inMember);
            }
        }

        return made.IsEmpty ? state : state.WithObjects(made);
    }

    // Array.Copy into the array of a static field puts each element of the source into it. A read
    // through each name of the field then gets them.
    private void CopiesIntoAStaticField(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (ArgumentFor(method: method, arguments: arguments, parameter: "destinationArray") is { } destination
            && this.IsAStaticField(destination)
            && ArgumentFor(method: method, arguments: arguments, parameter: "sourceArray") is { } source)
        {
            this.Absorb(target: destination, state: this.Eval(source).AsOneOfItsElements);
        }
    }
}
