using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     What an object built in a loop region holds. The evaluator finds it
///     from the constructor, with the parameters bound to the arguments.
/// </summary>
/// <param name="members">
///     The state of each field and property that an initializer
///     or the constructor sets.
/// </param>
/// <param name="followedParameters">
///     The parameters with all uses in one of those
///     initializers. The value went only into the object.
/// </param>
internal sealed class Construction(
    in ImmutableDictionary<ISymbol, LoopState> members,
    in ImmutableHashSet<ISymbol> followedParameters)
{
    private readonly ImmutableDictionary<ISymbol, LoopState> members = members;

    /// <summary>An object with no member that the evaluator knows.</summary>
    public static Construction Empty { get; } =
        new(
            members: ImmutableDictionary.Create<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            followedParameters: ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default));

    public ImmutableHashSet<ISymbol> FollowedParameters { get; } = followedParameters;

    /// <summary>The state of each member that the evaluator knows.</summary>
    public IEnumerable<KeyValuePair<ISymbol, LoopState>> Members => this.members;

    /// <summary>
    ///     The same object after an initializer sets one member. The new state
    ///     replaces the state from the constructor.
    /// </summary>
    public Construction With(ISymbol member, LoopState state) =>
        new(
            members: this.members.SetItem(key: member.OriginalDefinition, value: state),
            followedParameters: this.FollowedParameters);

    public LoopState StateOf(ISymbol member) =>
        this.members.TryGetValue(key: member.OriginalDefinition, value: out LoopState? state) ? state : LoopState.None;

    /// <summary>
    ///     The members of two objects that one expression can name, such as the
    ///     two arms of a conditional. A member of either is kept, with the states
    ///     joined.
    /// </summary>
    public static Construction? Merge(Construction? first, Construction? second)
    {
        if (first == null || ReferenceEquals(objA: first, objB: second))
        {
            return second;
        }

        if (second == null)
        {
            return first;
        }

        ImmutableDictionary<ISymbol, LoopState>.Builder merged = first.members.ToBuilder();

        foreach (KeyValuePair<ISymbol, LoopState> pair in second.members)
        {
            merged[pair.Key] =
                merged.TryGetValue(key: pair.Key, value: out LoopState? existing)
                    ? existing.Join(pair.Value)
                    : pair.Value;
        }

        return new Construction(
            members: merged.ToImmutable(),
            followedParameters: first.FollowedParameters.Intersect(second.FollowedParameters));
    }
}
