using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The state of a member of the element at a location that the walk can
///     read again, as in <c>list[0].Share</c>. The last sure write through that
///     location gave the state. Each write after it that can be to the same
///     object added its state.
/// </summary>
/// <param name="collection">The local or the parameter of the collection.</param>
/// <param name="index">The key of a constant index, or the local that has the index.</param>
/// <param name="member">The member.</param>
/// <param name="end">The position of the end of the assignment.</param>
/// <param name="state">The state of the member.</param>
internal sealed class ElementMemberWrite(
    in ISymbol collection,
    in object index,
    in ISymbol member,
    in int end,
    in LoopState state)
{
    private readonly ISymbol collection = collection;
    private readonly object index = index;

    public ISymbol Member { get; } = member;

    public int End { get; } = end;

    public LoopState State { get; } = state;

    /// <summary>The same write, with a second state of the member added.</summary>
    public ElementMemberWrite Adding(LoopState added) =>
        new(
            collection: this.collection,
            index: this.index,
            member: this.Member,
            end: this.End,
            state: this.State.Join(added));

    /// <summary>True for the member of the element at a location.</summary>
    public bool IsOf(ISymbol collection, object index, ISymbol member) =>
        SymbolEqualityComparer.Default.Equals(x: this.collection, y: collection)
        && SymbolEqualityComparer.Default.Equals(x: this.Member, y: member)
        && (index is ISymbol local
            ? this.index is ISymbol before && SymbolEqualityComparer.Default.Equals(x: local, y: before)
            : index.Equals(this.index));
}
