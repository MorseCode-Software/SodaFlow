using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     A write to a member of an object through a local or a parameter, as in
///     <c>row.Share = value</c>, that always runs in its function. The next
///     write through the same name to the same member can replace it.
/// </summary>
/// <remarks>
///     The name can be one of two or more objects of the store, and the write
///     adds its state to each of them. The write keeps the state that each
///     object had before, and the state that it gave. An object that has that
///     second state did not change after the write.
/// </remarks>
/// <param name="name">The local or the parameter.</param>
/// <param name="member">The member.</param>
/// <param name="code">The function whose first level has the write.</param>
/// <param name="position">The position of the name in its file.</param>
/// <param name="objects">
///     Each object of the store that the name can be, with its state before the
///     write and its state after the write.
/// </param>
internal sealed class WriteThroughAName(
    in ISymbol name,
    in ISymbol member,
    in SyntaxNode code,
    in int position,
    in IReadOnlyList<(MadeObject Object, LoopState Before, LoopState After)> objects)
{
    public ISymbol Name { get; } = name;

    public ISymbol Member { get; } = member;

    public SyntaxNode Code { get; } = code;

    public int Position { get; } = position;

    public IReadOnlyList<(MadeObject Object, LoopState Before, LoopState After)> Objects { get; } = objects;
}
