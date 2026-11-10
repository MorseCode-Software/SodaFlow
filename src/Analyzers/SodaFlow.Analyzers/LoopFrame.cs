using System.Collections.Generic;

namespace SodaFlow.Analyzers;

/// <summary>
///     The code of one call that the walk reads. It is the block, or the body
///     of a method, a constructor, or a getter that the block calls. A local
///     function and a lambda are part of the frame whose code contains them.
/// </summary>
/// <remarks>
///     A frame is an identity: a finding keeps the frame that its origin is a
///     name of.
/// </remarks>
internal sealed class LoopFrame
{
    private readonly List<QueryValue> waiting = [];

    /// <summary>
    ///     The queries that the code of this frame made or got from a callee. When
    ///     the code of a callee returns, the walk reads each one that nothing
    ///     enumerated, and the query keeps what the walk found. The block itself
    ///     has no such step: a query that the block only keeps runs after the
    ///     close.
    /// </summary>
    public IReadOnlyList<QueryValue> Waiting => this.waiting;

    public void Wait(QueryValue query) => this.waiting.Add(query);

    /// <summary>
    ///     True after the walk of the code of this frame. A function that the
    ///     code made can run after that, when the caller kept it. Then, no walk
    ///     of this frame gets what the function finds.
    /// </summary>
    public bool HasReturned { get; private set; }

    public void Return() => this.HasReturned = true;
}
