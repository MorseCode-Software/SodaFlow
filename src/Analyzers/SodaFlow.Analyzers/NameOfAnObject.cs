using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     A name that holds an object. It is a local or a parameter, a static
///     field, or a field of the object of a local. It can also be an element at
///     a location that the walk can read again.
/// </summary>
/// <param name="of">The value of <see cref="Of" />.</param>
/// <param name="owner">The value of <see cref="Owner" />.</param>
/// <param name="index">The value of <see cref="Index" />.</param>
/// <param name="code">The function whose code has the name.</param>
/// <param name="from">The position from which the name holds the object.</param>
internal sealed class NameOfAnObject(
    in ISymbol of,
    in ISymbol? owner,
    in object? index,
    in SyntaxNode code,
    in int from)
{
    /// <summary>
    ///     The local, the parameter, or the field. For an element, the local,
    ///     the parameter, or the static field of the collection.
    /// </summary>
    public ISymbol Of { get; } = of;

    /// <summary>
    ///     The local or the parameter whose object has the field. Null for each
    ///     other name.
    /// </summary>
    public ISymbol? Owner { get; } = owner;

    /// <summary>
    ///     The key of a constant index, or the local that has the index. Null
    ///     for a name that is no element.
    /// </summary>
    public object? Index { get; } = index;

    public SyntaxNode Code { get; } = code;

    public int From { get; } = from;

    /// <summary>True for the name with the given parts.</summary>
    public bool Is(ISymbol of, ISymbol? owner, object? index) =>
        SymbolEqualityComparer.Default.Equals(x: this.Of, y: of)
        && this.HasTheIndexAndTheOwner(owner: owner, index: index);

    /// <summary>
    ///     True when the name has the given owner and the given index. Two
    ///     elements with the same index of one collection are one name.
    /// </summary>
    public bool HasTheIndexAndTheOwner(ISymbol? owner, object? index) =>
        SymbolEqualityComparer.Default.Equals(x: this.Owner, y: owner)
        && (this.Index is ISymbol local
            ? index is ISymbol other && SymbolEqualityComparer.Default.Equals(x: local, y: other)
            : Equals(objA: this.Index, objB: index));
}
