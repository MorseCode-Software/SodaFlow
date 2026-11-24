using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a run of the timers or a Dispose in a callee. The run is in the
// branch of a condition whose value the walk does not know. See SinkWalker.UndecidedRuns.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The run of a callback of a timer, read by an evaluator that changes
    ///     nothing that the walk keeps.
    /// </summary>
    /// <param name="call">The call of RunTimersTo.</param>
    /// <param name="callback">The expression of the callback at its SetTimer.</param>
    /// <param name="functions">The state of the callback.</param>
    public Followed? FollowAnUndecidedCallback(SyntaxNode call, ExpressionSyntax callback, LoopState functions) =>
        this.Quiet().FollowCallback(call: call, callback: callback, functions: functions);

    /// <summary>
    ///     The run of the eviction handlers of the maps that a Dispose disposes
    ///     only where a condition is true. The walk does not know the value of
    ///     that condition. An evaluator that changes nothing reads the run.
    /// </summary>
    /// <param name="map">The expression of the map.</param>
    /// <param name="dispose">The call of Dispose.</param>
    /// <param name="runsOnEachPath">True when the Dispose runs on all paths of its function.</param>
    /// <param name="whereTheUndecidedHold">
    ///     Gives the value of a test where each condition whose value the walk
    ///     does not know is true.
    /// </param>
    public Followed? FollowEvictionsOfAnUndecidedDispose(
        ExpressionSyntax map,
        SyntaxNode dispose,
        Func<bool> runsOnEachPath,
        Func<Func<bool>, bool> whereTheUndecidedHold)
    {
        bool? sure = null;

        // A Dispose disposes each object of this code, also with no condition: see ObjectsOfADispose.
        // An object that the Dispose disposes for sure has its run there. A second walk of it here
        // gives an info at the location of each warning of that run, which the warning wins.
        MadeObject[] undecided =
        [
            .. this.region.Objects.Now(this.Eval(map).Objects).Where(made =>
                !made.IsFrom(this.Frame)
                && !((sure ??= runsOnEachPath()) && this.CallsRunFor(owns: made.IsFrom, forARun: true))
                && whereTheUndecidedHold(() =>
                    runsOnEachPath() && this.CallsRunFor(owns: made.IsFrom, forARun: true)))
        ];

        return undecided.Length == 0
            ? null
            : this.Quiet()
                .FollowEvictions(
                    map: map,
                    dispose: dispose,
                    handlers: undecided.SelectMany(made => this.region.Objects.HandlersOfAMap(made)));
    }
}
