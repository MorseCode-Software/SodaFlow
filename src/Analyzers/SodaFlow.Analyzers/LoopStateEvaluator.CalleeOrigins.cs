using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the origin of a member that a callee or a function of a different object reads. The
// origin is the argument that the caller gave, where the walk knows it.
internal sealed partial class LoopStateEvaluator
{
    // The objects of the store that this is, for a callee or a reader of a build on a different object. Empty for each
    // other code. See MembersOfItsObjectNow.
    private ImmutableArray<MadeObject> objectsOfThis = ImmutableArray<MadeObject>.Empty;

    // Each member of this that a callee on the same object wrote, also through its own callees. See
    // TakesTheOriginsOfACallee.
    private HashSet<ISymbol>? writtenByACallee;

    /// <summary>
    ///     Changes the origin of each member that a callee on this object
    ///     wrote from a parameter of the callee to the origin of its argument.
    /// </summary>
    /// <remarks>
    ///     A callee on this object writes the members of this code, and not a
    ///     copy. Thus, a value from a parameter of the callee keeps that
    ///     parameter as its origin after the call. A read after the call then
    ///     gets the origin of a member, and not of the argument. A setter and a
    ///     method that set a member are such callees.
    /// </remarks>
    /// <param name="inner">The evaluator of the callee.</param>
    /// <param name="byParameter">The binding of each parameter of the callee.</param>
    private void TakesTheOriginsOfACallee(LoopStateEvaluator inner, IReadOnlyDictionary<ISymbol, Binding> byParameter)
    {
        if (!ReferenceEquals(objA: inner.thisMembers, objB: this.thisMembers)
            || (inner.writesThroughThis == null && inner.writtenByACallee == null))
        {
            return;
        }

        foreach (ISymbol key in WrittenIn(inner))
        {
            if (!this.thisMembers.TryGetValue(key: key, value: out LoopState? state))
            {
                continue;
            }

            bool changes = false;

            LoopState changed = state.WithOrigins(origin =>
            {
                if (origin == null || !byParameter.TryGetValue(key: origin, value: out Binding given))
                {
                    return origin;
                }

                changes = true;

                return given.State.AnyOrigin;
            });

            if (changes)
            {
                this.thisMembers[key] = changed;
            }

            (this.writtenByACallee ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default)).Add(key);
        }

        return;

        static IEnumerable<ISymbol> WrittenIn(LoopStateEvaluator callee)
        {
            foreach (ISymbol key in callee.writesThroughThis?.Keys ?? (IEnumerable<ISymbol>)[])
            {
                yield return key;
            }

            foreach (ISymbol key in callee.writtenByACallee ?? [])
            {
                yield return key;
            }
        }
    }

    /// <summary>
    ///     The members that the object of this code has now in the store, for
    ///     each member that the given record does not have. Empty when this code
    ///     is not on an object of the store.
    /// </summary>
    /// <remarks>
    ///     A function of this code can run after the code returned, at a read of
    ///     a value that a different object holds. That object gives the function
    ///     its own members, and not the members of the object of this code. A
    ///     write to a member of the object of this code after the return is then
    ///     only in the store. The caller joins these states with the record of
    ///     this code, thus nothing that the record has goes away.
    /// </remarks>
    /// <param name="but">The members that the function gets from the object that holds the value.</param>
    private IReadOnlyDictionary<ISymbol, LoopState> MembersOfItsObjectNow(IReadOnlyDictionary<ISymbol, LoopState> but)
    {
        Dictionary<ISymbol, LoopState> members = new(SymbolEqualityComparer.Default);

        if (this.objectsOfThis.IsEmpty)
        {
            return members;
        }

        foreach (KeyValuePair<ISymbol, LoopState> member in this.region.Objects.Now(this.objectsOfThis)
                     .SelectMany(made => this.region.Objects.StateOf(made)?.Members?.Members ?? [])
                     .Where(member => !but.ContainsKey(member.Key)))
        {
            members[member.Key] =
                members.TryGetValue(key: member.Key, value: out LoopState? other)
                    ? other.Join(member.Value)
                    : member.Value;
        }

        return members;
    }
}
