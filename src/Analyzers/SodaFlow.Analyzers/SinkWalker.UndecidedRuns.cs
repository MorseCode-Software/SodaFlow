using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for a run of the timers or a Dispose in a callee. The run is in the branch
// of a condition whose value the walk does not know. The walk reads such a run with an evaluator that
// changes nothing. Each read of the run is an info, SF1007. The state after the run is the state
// before it.
internal sealed partial class SinkWalker
{
    // True when the code runs where each condition whose value the walk does not know is true.
    private bool RunsWhereTheUndecidedHold(Func<bool> runs)
    {
        this.region.CountsAnUndecidedCondition = true;

        try
        {
            return runs();
        }
        finally
        {
            this.region.CountsAnUndecidedCondition = false;
        }
    }

    // Gives the sink each read of a run that maybe does not occur, with the mark of such a read. An
    // object that such a run throws does not come to this code.
    private void ReportTheUndecided(Followed? followed)
    {
        if (followed == null)
        {
            return;
        }

        foreach (Finding finding in followed.Findings)
        {
            this.sink(finding.AsUndecided());
        }
    }

    // The timers that a call of RunTimersTo runs only where a condition is true, and the walk does not
    // know the value of that condition. The walk reads each callback with an evaluator that changes
    // nothing. Thus, the clock keeps each such timer.
    private void RunsTheUndecidedTimers(SyntaxNode call, IEnumerable<KeptTimer> timers)
    {
        foreach (KeptTimer timer in timers.Where(timer =>
                     this.RunsWhereTheUndecidedHold(() =>
                         this.SurelyRuns(call: call, forARun: true, after: out _)
                         && this.evaluator.CallsRunFor(owns: timer.Keeper.evaluator.Frame.IsWithin, forARun: true))))
        {
            this.ReportTheUndecided(
                this.evaluator.FollowAnUndecidedCallback(
                    call: call,
                    callback: timer.Callback,
                    functions: timer.Functions));
        }
    }

    // The same for the objects of a Dispose.
    private void DisposesTheUndecided(ExpressionSyntax map, SyntaxNode dispose, Func<bool> runsOnEachPath) =>
        this.ReportTheUndecided(
            this.evaluator.FollowEvictionsOfAnUndecidedDispose(
                map: map,
                dispose: dispose,
                runsOnEachPath: runsOnEachPath,
                whereTheUndecidedHold: this.RunsWhereTheUndecidedHold));
}
