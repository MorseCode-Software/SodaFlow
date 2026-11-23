using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for a remove of a handler that a call of a function of this code runs.
internal sealed partial class SinkWalker
{
    // True after a += of a function in this code, or a local that starts with a function that it did
    // not make. A call can remove a handler only after that. The flag is only for speed: the rules of
    // a remove count only an add that the walk read before the call.
    private bool addsAHandler;

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        base.VisitExpressionStatement(node);

        // A call that each run of the code runs, of a function whose first statement removes a
        // handler, takes the handler away here: see AfterARemoveThroughACall.
        if (!this.addsAHandler
            || node.Expression is not InvocationExpressionSyntax call
            || this.evaluator.AfterARemoveThroughACall(call) is not var (target, member, state)
            || !this.RemovesOnEachPath(call))
        {
            return;
        }

        if (member != null)
        {
            this.evaluator.SetMemberOf(owner: target, member: member, state: state, replaces: true, turn: null);
        }
        else
        {
            this.AssignState(target: target, state: state, replaces: true, turn: null);
        }
    }
}
