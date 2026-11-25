using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a method group of the object of this through a different name. An example is m.Add in
// the code that made m. A first read in a method of that object runs the group with the members of this.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The function of a first read of a value, as a method group of this.
    ///     The group must name the object of this through a different name.
    ///     Each other function stays as it is.
    /// </summary>
    /// <remarks>
    ///     A group of a different name reads the members that the store has for
    ///     its object. A method of that object keeps its writes to the members of
    ///     this in its own record until it returns. Thus, at a read in that
    ///     method, the store does not have the writes. A group of this reads the
    ///     record. The walk must know the one object of each of the two names.
    /// </remarks>
    /// <param name="function">The function that the read runs.</param>
    /// <param name="at">The expression of the value at the read.</param>
    private FunctionValue AsAGroupOfThis(FunctionValue function, SyntaxNode at)
    {
        if (function.OfThis != null
            || OwnerOfAGroup(function) is not { } owner
            || !owner.Equals(this.OwnerOfThis(at)))
        {
            return function;
        }

        // An extension method has no this. Its first parameter gets the receiver. Thus, the receiver is
        // this, with the members of the record, as an argument of this is.
        return function.Extended != null
            ? new FunctionValue(
                declaration: function.Declaration,
                definer: null,
                receiver: LoopState.None.WithMembers(
                    Construction.Of(this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default))),
                ofThis: null,
                owner: function.Owner,
                extended: function.Extended)
            : new FunctionValue(
                declaration: function.Declaration,
                definer: null,
                receiver: null,
                ofThis: this,
                owner: null);
    }
}
