using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>The result of a call whose body the walk read.</summary>
/// <param name="state">What the call returns, or the object that a constructor makes.</param>
/// <param name="findings">What the walk found in the body, as the caller reports it.</param>
/// <param name="outs">
///     The state that the body gives to each <c>out</c> or <c>ref</c> parameter.
/// </param>
internal sealed class Followed(
    in LoopState state,
    in ImmutableArray<Finding> findings,
    in ImmutableDictionary<ISymbol, LoopState> outs)
{
    /// <summary>
    ///     A call that the walk did not read, because no argument has a state.
    ///     Nothing from the loop can be in its result.
    /// </summary>
    public static Followed Nothing { get; } =
        new(
            state: LoopState.None,
            findings: ImmutableArray<Finding>.Empty,
            outs: ImmutableDictionary.Create<ISymbol, LoopState>(SymbolEqualityComparer.Default));

    public LoopState State { get; } = state;

    public ImmutableArray<Finding> Findings { get; } = findings;

    public ImmutableDictionary<ISymbol, LoopState> Outs { get; } = outs;
}
