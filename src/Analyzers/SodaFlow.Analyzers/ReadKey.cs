using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The name through which the walk reads a cell or a <c>Lazy</c>. It is a
///     local, a parameter, or a field of <c>this</c>. It can also be an element
///     of a local or a parameter at a constant index. A first read through the
///     name records the run for that name only.
/// </summary>
/// <param name="name">The local, the parameter, or the field.</param>
/// <param name="index">The constant index of the element, or null for no element.</param>
internal sealed class ReadKey(in ISymbol name, in object? index)
{
    public ISymbol Name { get; } = name;

    private object? Index { get; } = index;

    /// <summary>True when the two keys are for the same name.</summary>
    public bool IsTheSameAs(ReadKey other) =>
        Equals(objA: this.Index, objB: other.Index) && SymbolEqualityComparer.Default.Equals(x: this.Name, y: other.Name);
}
