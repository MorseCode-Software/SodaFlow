using System.Collections.Immutable;

namespace SodaFlow.Analyzers;

/// <summary>The result of a call whose body the walk read.</summary>
/// <param name="state">What the call returns, or the object that a constructor makes.</param>
/// <param name="findings">What the walk found in the body, as the caller reports it.</param>
internal sealed class Followed(in LoopState state, in ImmutableArray<Finding> findings)
{
    /// <summary>
    ///     A call that the walk did not read, because no argument has a state.
    ///     Nothing from the loop can be in its result.
    /// </summary>
    public static Followed Nothing { get; } = new(state: LoopState.None, findings: ImmutableArray<Finding>.Empty);

    public LoopState State { get; } = state;

    public ImmutableArray<Finding> Findings { get; } = findings;
}
