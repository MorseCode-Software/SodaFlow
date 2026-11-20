using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     A name that the walk can read again: a local or a parameter, a field,
///     or an element at a constant index. The owner is the local of an
///     instance field, and the index is -1 for each name that is no element.
///     For an element at an index that a local keeps, the owner is that local
///     and the index is -2.
/// </summary>
/// <param name="of">The local, the parameter, the field, or the collection.</param>
/// <param name="owner">The local that has the field, the local of the index, or null.</param>
/// <param name="index">The constant index, -1, or -2.</param>
internal readonly struct NameKey(in ISymbol of, in ISymbol? owner, in int index)
{
    public ISymbol Of { get; } = of;

    public ISymbol? Owner { get; } = owner;

    public int Index { get; } = index;

    /// <summary>
    ///     True when this name is the given name. It is true also for an element
    ///     of the given name and for a field of it. Such a name names a different
    ///     object when the given name gets a different value.
    /// </summary>
    public bool IsIn(NameKey name) =>
        this.IsTheSameAs(name)
        || (name is { Owner: null, Index: < 0 }
            && (SymbolEqualityComparer.Default.Equals(x: this.Of, y: name.Of)
                || SymbolEqualityComparer.Default.Equals(x: this.Owner, y: name.Of)));

    /// <summary>True when the two keys are for the same name.</summary>
    public bool IsTheSameAs(NameKey other) =>
        this.Index == other.Index
        && SymbolEqualityComparer.Default.Equals(x: this.Of, y: other.Of)
        && SymbolEqualityComparer.Default.Equals(x: this.Owner, y: other.Owner);
}
