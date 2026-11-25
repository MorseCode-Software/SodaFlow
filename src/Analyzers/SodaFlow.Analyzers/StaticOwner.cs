using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The owner of a write: the object of a static field, or of an element of
///     the array of a static field. No code gives such a location a different
///     object: see <see cref="UnchangedStatics" />.
/// </summary>
/// <remarks>
///     The initializer makes a different object for each such location. Thus,
///     two owners that are not equal are two different objects.
/// </remarks>
/// <param name="field">The field.</param>
/// <param name="index">The position of the element, or -1 for the field itself.</param>
internal sealed class StaticOwner(in ISymbol field, in int index)
{
    private readonly ISymbol field = field.OriginalDefinition;
    private readonly int index = index;

    public override bool Equals(object? obj) =>
        obj is StaticOwner other
        && other.index == this.index
        && SymbolEqualityComparer.Default.Equals(x: other.field, y: this.field);

    public override int GetHashCode() =>
        unchecked(SymbolEqualityComparer.Default.GetHashCode(this.field) * 31 + this.index);
}
