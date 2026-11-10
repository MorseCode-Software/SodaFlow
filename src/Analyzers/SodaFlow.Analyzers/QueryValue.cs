using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     A sequence that runs code of this compilation when something enumerates
///     it, and not when the code makes it. It is a query of the platform, a
///     query expression, or the result of an iterator.
/// </summary>
/// <remarks>
///     A query is an identity: the walk reads what one query runs one time. For
///     a query of the block, that is where the code enumerates it. The findings
///     of a query that a callee made have the names of that callee. Thus, the
///     walk reads such a query when the callee returns. The query keeps the
///     findings in the names of the caller, and the walk reports them where the
///     code enumerates the query.
/// </remarks>
/// <param name="call">
///     The call or the query expression that makes the sequence.
/// </param>
/// <param name="definer">
///     The evaluator of the code that contains the call. What the query runs
///     reads the locals of that code.
/// </param>
internal sealed class QueryValue(in SyntaxNode call, in LoopStateEvaluator definer)
{
    private ImmutableArray<Finding>? held;
    private bool started;

    public SyntaxNode Call { get; } = call;

    public LoopStateEvaluator Definer { get; } = definer;

    /// <summary>
    ///     True at the first call. The walk then reads what the query runs. False
    ///     at each call after that, and for a query that keeps its findings.
    /// </summary>
    public bool Start()
    {
        if (this.started)
        {
            return false;
        }

        this.started = true;

        return true;
    }

    /// <summary>
    ///     Gives the query the findings of its code, in the names of the code that
    ///     has the query now.
    /// </summary>
    public void Hold(ImmutableArray<Finding> findings)
    {
        this.started = true;
        this.held = findings;
    }

    /// <summary>
    ///     The findings that the query keeps. The query then keeps none. Null for
    ///     a query that keeps none.
    /// </summary>
    public ImmutableArray<Finding>? TakeHeld()
    {
        ImmutableArray<Finding>? findings = this.held;
        this.held = null;

        return findings;
    }
}
