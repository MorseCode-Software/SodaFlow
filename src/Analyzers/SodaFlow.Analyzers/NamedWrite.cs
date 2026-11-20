using System.Collections.Immutable;

namespace SodaFlow.Analyzers;

/// <summary>
///     A write to a member through a name, which gave the region its state for
///     the member.
/// </summary>
/// <param name="name">The name of the owner.</param>
/// <param name="objects">The objects of the store that the owner can be.</param>
/// <param name="at">The number of the write, from <see cref="LoopRegion.MemberWrites" />.</param>
internal sealed class NamedWrite(in NameKey name, in ImmutableArray<MadeObject> objects, in int at)
{
    public NameKey Name { get; } = name;

    public ImmutableArray<MadeObject> Objects { get; } = objects;

    public int At { get; } = at;
}
