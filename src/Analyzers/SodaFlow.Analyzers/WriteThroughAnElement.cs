using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     A write to a member through an element of a collection, as in
///     <c>list[0].Share = value</c>. A read through the same element, with no
///     code between the two, reads what the write gave.
/// </summary>
/// <param name="collection">The local or the parameter of the collection.</param>
/// <param name="index">The index: a constant number, or the local that has it.</param>
/// <param name="member">The member.</param>
/// <param name="code">The function that has the write in its code.</param>
/// <param name="end">The position of the end of the assignment.</param>
/// <param name="state">The state of the value.</param>
/// <param name="writes">The number of writes to a member in the region, after this write.</param>
internal sealed class WriteThroughAnElement(
    in ISymbol collection,
    in object index,
    in ISymbol member,
    in SyntaxNode code,
    in int end,
    in LoopState state,
    in int writes)
{
    public ISymbol Collection { get; } = collection;

    public object Index { get; } = index;

    public ISymbol Member { get; } = member;

    public SyntaxNode Code { get; } = code;

    public int End { get; } = end;

    public LoopState State { get; } = state;

    public int Writes { get; } = writes;
}
