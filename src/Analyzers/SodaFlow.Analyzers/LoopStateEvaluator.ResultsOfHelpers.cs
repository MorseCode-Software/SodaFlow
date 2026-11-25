using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the results of a helper that the walk does not read. Here, a second call of the helper
// is in a different function. See NotesAnOlderResult for two calls in one function.
internal sealed partial class LoopStateEvaluator
{
    // Records the call that gave an object of the store. The walk does not read the helper, and a second call of it
    // can give the same object.
    private void NotesAnUnreadResult(InvocationExpressionSyntax call, LoopState made)
    {
        if (this.quiet)
        {
            return;
        }

        foreach (MadeObject one in made.Objects)
        {
            this.region.UnreadResults[one] = call;
        }
    }

    // Records a write to a member through the result of a call of such a helper, or through a local of one. Most
    // regions have no such result, thus that test comes first.
    private void NotesAWriteThroughAResult(ExpressionSyntax? written)
    {
        if (this.region.UnreadResults.Count > 0
            && !this.quiet
            && written != null
            && this.CallThatGave(written) is { } call)
        {
            this.region.WritesThroughResults.Add(call);
        }
    }

    // True when an owner can be the result of a call of a helper. A write through the result of a second call of that
    // helper can then be a write to the same object: see CanGiveOneObject. The region has the state of that write for
    // the member. A write through the result of the same call is a write through a name of the owner. The records of
    // names have such a write, also through a local of that result.
    private bool IsWrittenThroughASecondResult(LoopState owner) =>
        this.region.WritesThroughResults.Count > 0
        && owner.Objects.Any(made =>
            this.region.UnreadResults.TryGetValue(key: made, value: out InvocationExpressionSyntax? first)
            && this.region.WritesThroughResults.Any(write =>
                write != first && this.CanGiveOneObject(one: first, other: write)));
}
