using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     What an object built in a loop region holds. The evaluator finds it from the
///     constructor, with the parameters bound to the arguments.
/// </summary>
/// <param name="members">The state of each field and property that an initializer or the constructor sets.</param>
/// <param name="followedParameters">
///     The parameters with all uses in one of those initializers. The value went
///     only into the object.
/// </param>
internal sealed class Construction(
    in ImmutableDictionary<ISymbol, LoopState> members,
    in ImmutableHashSet<ISymbol> followedParameters)
{
    public ImmutableDictionary<ISymbol, LoopState> Members { get; } = members;

    public ImmutableHashSet<ISymbol> FollowedParameters { get; } = followedParameters;

    /// <summary>
    ///     The object as one value: pending or linked when any member is. A hold of
    ///     the object, or a cell of such objects, then knows what it holds.
    /// </summary>
    public LoopState Summary
    {
        get
        {
            LoopState summary = LoopState.None;

            foreach (LoopState state in this.Members.Values)
            {
                summary = summary.Join(state);
            }

            return new LoopState(
                Pending: summary.Pending || summary.ContentPending,
                Linked: summary.Linked || summary.ContentLinked,
                ContentPending: false,
                ContentLinked: false);
        }
    }

    public LoopState StateOf(ISymbol member) =>
        this.Members.TryGetValue(member.OriginalDefinition, out LoopState? state) ? state : LoopState.None;

    /// <summary>
    ///     The members of two objects that one expression can name, such as the two
    ///     arms of a conditional. A member of either is kept, with the states joined.
    /// </summary>
    public static Construction? Merge(Construction? first, Construction? second)
    {
        if (first == null || ReferenceEquals(first, second))
        {
            return second;
        }

        if (second == null)
        {
            return first;
        }

        ImmutableDictionary<ISymbol, LoopState>.Builder members = first.Members.ToBuilder();

        foreach (System.Collections.Generic.KeyValuePair<ISymbol, LoopState> pair in second.Members)
        {
            members[pair.Key] = members.TryGetValue(pair.Key, out LoopState? existing) ? existing.Join(pair.Value) : pair.Value;
        }

        return new Construction(
            members: members.ToImmutable(),
            followedParameters: first.FollowedParameters.Intersect(second.FollowedParameters));
    }
}
