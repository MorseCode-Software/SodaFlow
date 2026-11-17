using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>The result of a call whose body the walk read.</summary>
/// <param name="state">What the call returns, or the object that a constructor makes.</param>
/// <param name="findings">What the walk found in the body, as the caller reports it.</param>
/// <param name="outs">
///     The state that the body gives to each <c>out</c> or <c>ref</c> parameter.
/// </param>
/// <param name="alwaysGives">The value of <see cref="Followed.AlwaysGives" />.</param>
internal sealed class Followed(
    in LoopState state,
    in ImmutableArray<Finding> findings,
    in ImmutableDictionary<ISymbol, LoopState> outs,
    in ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)> sets,
    in ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State)> through,
    in ImmutableHashSet<ISymbol> alwaysGives)
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
            sets: ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)>.Empty,
            through: ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State)>.Empty,
            alwaysGives: ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default));

    public LoopState State { get; } = state;

    public ImmutableArray<Finding> Findings { get; } = findings;

    public ImmutableDictionary<ISymbol, LoopState> Outs { get; } = outs;

    /// <summary>
    ///     Each member of its object that the callee set, with the state of the
    ///     member at the return of the callee. Each write of the callee is in
    ///     that state. The last part is the one collection that the member then
    ///     is, when a write that always runs gave it.
    /// </summary>
    public ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)> Sets { get; } = sets;

    /// <summary>
    ///     Each member that the callee set through a parameter, with the state
    ///     of the member at the return of the callee.
    /// </summary>
    public ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State)> Through { get; } = through;

    /// <summary>
    ///     Each parameter with <c>ref</c> or <c>out</c> that the body always
    ///     gives the value in <see cref="Outs" />. No position of a collection
    ///     changes after the last such assignment. Thus, the location of the
    ///     argument has that value after the call.
    /// </summary>
    public ImmutableHashSet<ISymbol> AlwaysGives { get; } = alwaysGives;
}
