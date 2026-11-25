using System.Collections.Immutable;

namespace SodaFlow.Analyzers;

/// <summary>
///     A write to a member through a name, which gave the region its state for
///     the member.
/// </summary>
/// <param name="name">The name of the owner, or null for each name.</param>
/// <param name="objects">The objects of the store that the owner can be.</param>
/// <param name="at">The number of the write, from <see cref="LoopRegion.MemberWrites" />.</param>
/// <param name="canBeAnUnknownObject">The value of <see cref="CanBeAnUnknownObject" />.</param>
internal sealed class NamedWrite(
    in NameKey? name,
    in ImmutableArray<MadeObject> objects,
    in int at,
    in bool canBeAnUnknownObject)
{
    public NameKey? Name { get; } = name;

    public ImmutableArray<MadeObject> Objects { get; } = objects;

    public int At { get; } = at;

    /// <summary>
    ///     True for a write through an element whose value the walk does not
    ///     know, as in a list that gets an Add in a branch. The owner can then
    ///     be each object that the store does not know, with each name.
    /// </summary>
    public bool CanBeAnUnknownObject { get; } = canBeAnUnknownObject;
}
