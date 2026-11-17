using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The array of a foreach statement over a name, as the loop starts. Each
///     turn reads an element of that array, also after the body gives the name
///     a different array.
/// </summary>
/// <param name="name">The name of the array in the foreach statement.</param>
/// <param name="state">The state of the array at the start of the loop.</param>
/// <param name="one">
///     The one collection of the name at the start of the loop. Null when the
///     name has no such collection.
/// </param>
internal sealed class ArrayOfAForeach(in IdentifierNameSyntax name, in LoopState state, in MadeObject? one)
{
    public IdentifierNameSyntax Name { get; } = name;

    public LoopState State { get; } = state;

    public MadeObject? One { get; } = one;
}
