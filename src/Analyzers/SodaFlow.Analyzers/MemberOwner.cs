using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The owner of a write: the object in a field of a known owner. The
///     initializer of the field makes that object, and no code can give the
///     field a different object.
/// </summary>
/// <remarks>
///     Each owner has its own object in the field. Thus, two owners that are not
///     equal are two different objects.
/// </remarks>
/// <param name="owner">The owner of the field.</param>
/// <param name="field">The field.</param>
internal sealed class MemberOwner(in object owner, in ISymbol field)
{
    private readonly ISymbol field = field.OriginalDefinition;
    private readonly object owner = owner;

    public override bool Equals(object? obj) =>
        obj is MemberOwner other
        && other.owner.Equals(this.owner)
        && SymbolEqualityComparer.Default.Equals(x: other.field, y: this.field);

    public override int GetHashCode() =>
        unchecked(this.owner.GetHashCode() * 31 + SymbolEqualityComparer.Default.GetHashCode(this.field));
}
