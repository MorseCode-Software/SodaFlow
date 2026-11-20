using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     Two names of one value, which an assignment from one name to the other
///     made.
/// </summary>
/// <param name="one">The name that got the value.</param>
/// <param name="other">The name that gave the value.</param>
/// <param name="code">The function that has the assignment.</param>
/// <param name="position">The end of the value in the assignment.</param>
internal sealed class NameLink(in NameKey one, in NameKey other, in SyntaxNode? code, in int position)
{
    public NameKey One { get; } = one;

    public NameKey Other { get; } = other;

    public SyntaxNode? Code { get; } = code;

    public int Position { get; } = position;
}
