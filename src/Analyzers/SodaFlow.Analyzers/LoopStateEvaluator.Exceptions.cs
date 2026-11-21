using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for an object that a callee throws through code of the platform. The platform can put
// the object into a different exception, or keep it in a task.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The result of a call that the runtime runs. The runtime replaces each
    ///     object that the callee throws with a new object of the type of the
    ///     wrapper. The caller gets that object.
    /// </summary>
    /// <param name="followed">The result of the callee.</param>
    /// <param name="wrapper">The metadata name of the type of the wrapper.</param>
    private Followed? Wrapped(Followed? followed, string wrapper)
    {
        if (followed == null || followed.Thrown.IsEmpty)
        {
            return followed;
        }

        ITypeSymbol? type = this.region.TypeOfThePlatform(wrapper);

        return WithThrown(
            followed: followed,
            thrown: followed.Thrown.Select(one => (one.Call, type, LoopState.None)).ToImmutableArray());
    }

    /// <summary>
    ///     The result of the function of a task that the call runs. The task
    ///     keeps each object that the function throws, and the call throws none.
    /// </summary>
    private static Followed? KeptByTheTask(Followed? followed) =>
        followed == null || followed.Thrown.IsEmpty
            ? followed
            : WithThrown(
                followed: followed,
                thrown: ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)>.Empty);

    private static Followed WithThrown(
        Followed followed,
        ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)> thrown) =>
        new(
            state: followed.State,
            findings: followed.Findings,
            outs: followed.Outs,
            sets: followed.Sets,
            through: followed.Through,
            alwaysGives: followed.AlwaysGives,
            thrown: thrown);
}
