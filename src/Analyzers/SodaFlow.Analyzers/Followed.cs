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
    in ImmutableDictionary<ISymbol, LoopState> outs,
    in ImmutableArray<(ISymbol Member, LoopState State, bool Replaces, MadeObject? One)> sets,
    in ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State, bool Replaces)> through)
{
    /// <summary>
    ///     A call that the walk did not read, because no argument has a state.
    ///     Nothing from the loop can be in its result.
    /// </summary>
    public static Followed Nothing { get; } =
        new(
            state: LoopState.None,
            findings: ImmutableArray<Finding>.Empty,
            outs: ImmutableDictionary.Create<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            sets: ImmutableArray<(ISymbol Member, LoopState State, bool Replaces, MadeObject? One)>.Empty,
            through: ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State, bool Replaces)>.Empty);

    public LoopState State { get; } = state;

    public ImmutableArray<Finding> Findings { get; } = findings;

    public ImmutableDictionary<ISymbol, LoopState> Outs { get; } = outs;

    /// <summary>
    ///     Each member of its object that the callee set, with the state that the
    ///     member has when the callee returns. The third part is true when a
    ///     write that always runs in the callee gave the member that state. The
    ///     last part is the one collection that the member then is.
    /// </summary>
    public ImmutableArray<(ISymbol Member, LoopState State, bool Replaces, MadeObject? One)> Sets { get; } = sets;

    /// <summary>
    ///     Each member that the callee set through a parameter, with the state
    ///     that the member has when the callee returns. The last part is true when
    ///     a write that always runs in the callee gave the member that state.
    /// </summary>
    public ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State, bool Replaces)> Through { get; } = through;
}
