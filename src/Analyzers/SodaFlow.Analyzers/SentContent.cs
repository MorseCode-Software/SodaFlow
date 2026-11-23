using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The content that the sends of one read of a block gave to its sinks. The
///     next read gives that content to each sink from the start.
/// </summary>
/// <remarks>
///     A send puts its value into the sink in the transaction of the block. At
///     the end of that transaction, each value that the code made from the sink
///     has the value of the send. That is true also for a value that the code
///     made before the send. The walk reads the code in its order, thus only a
///     second read can give such a value the content.
/// </remarks>
internal sealed class SentContent
{
    // The key is the call that made the sink, for a sink that is an object of the store.
    private readonly Dictionary<SyntaxNode, LoopState> toMade = [];

    // The key is a name of a sink whose creation the walk did not see. A local of the code around
    // the block is such a name, and so is a field.
    private readonly Dictionary<ISymbol, LoopState> toNames = new(SymbolEqualityComparer.Default);

    // The key is the call that made a sink with a coalesce function. The value has the content of each
    // send of a statement of the block into that sink. It is true for a send of other code.
    private readonly Dictionary<SyntaxNode, (Dictionary<SyntaxNode, LoopState> InTheBlock, bool Other)>
        coalescing = [];

    /// <summary>True when a send went into a sink that the walk did not see made.</summary>
    public bool HasNames => this.toNames.Count > 0;

    /// <summary>True when the walk saw a sink with a coalesce function made.</summary>
    public bool HasCoalescing => this.coalescing.Count > 0;

    /// <summary>The content that the sends gave to each sink that the call made.</summary>
    public LoopState To(SyntaxNode creation) =>
        this.toMade.TryGetValue(key: creation, value: out LoopState? content) ? content : LoopState.None;

    /// <summary>The content that the sends gave to the sink of a name.</summary>
    public LoopState To(ISymbol name) =>
        this.toNames.TryGetValue(key: name, value: out LoopState? content) ? content : LoopState.None;

    /// <summary>Adds the content of a send into a sink that the call made.</summary>
    public void Add(SyntaxNode creation, LoopState content) =>
        this.toMade[creation] = this.To(creation).Join(content.ForASecondRead);

    /// <summary>Adds the content of a send into the sink of a name.</summary>
    public void Add(ISymbol name, LoopState content) =>
        this.toNames[name] = this.To(name).Join(content.ForASecondRead);

    /// <summary>True when the call made a sink with a coalesce function.</summary>
    public bool Coalesces(SyntaxNode creation) => this.coalescing.ContainsKey(creation);

    /// <summary>Records that the call made a sink with a coalesce function.</summary>
    public void AddCoalescing(SyntaxNode creation)
    {
        if (!this.coalescing.ContainsKey(creation))
        {
            this.coalescing[creation] = ([], false);
        }
    }

    /// <summary>
    ///     Counts a send into a sink with a coalesce function that the call made.
    /// </summary>
    /// <param name="creation">The call that made the sink.</param>
    /// <param name="inTheBlock">
    ///     The send, for a statement of the block that always runs before the
    ///     close. Null for each other send.
    /// </param>
    /// <param name="content">The state of the sink after the send of the value.</param>
    public void Count(SyntaxNode creation, SyntaxNode? inTheBlock, LoopState content)
    {
        if (!this.coalescing.TryGetValue(
                key: creation,
                value: out (Dictionary<SyntaxNode, LoopState> InTheBlock, bool Other) sends))
        {
            return;
        }

        if (inTheBlock != null)
        {
            sends.InTheBlock[inTheBlock] =
                sends.InTheBlock.TryGetValue(key: inTheBlock, value: out LoopState? before)
                    ? before.Join(content.ForASecondRead)
                    : content.ForASecondRead;
        }
        else
        {
            this.coalescing[creation] = (sends.InTheBlock, true);
        }
    }

    /// <summary>
    ///     True when two sends can go into the sink that the call made in one
    ///     transaction. The coalesce function of the sink then runs.
    /// </summary>
    public bool CanCoalesce(SyntaxNode creation) =>
        this.coalescing.TryGetValue(
            key: creation,
            value: out (Dictionary<SyntaxNode, LoopState> InTheBlock, bool Other) sends)
        && (sends.Other || sends.InTheBlock.Count > 1);

    /// <summary>
    ///     True when each send into the sink that the call made is in the block,
    ///     and two or more of them always run. The sink then fires only what its
    ///     coalesce function returns.
    /// </summary>
    public bool AlwaysCoalesces(SyntaxNode creation) =>
        this.CanCoalesce(creation) && !this.coalescing[creation].Other;

    /// <summary>
    ///     The state of the sink after each of the two sends into the sink that
    ///     the call made, in the order that they run. Null unless
    ///     <see cref="AlwaysCoalesces" /> is true and the sink gets two sends,
    ///     not more.
    /// </summary>
    /// <remarks>
    ///     Each send is a statement of the block, thus the first in the source
    ///     runs first. The coalesce function gets its value first. With three
    ///     sends or more, the function also gets its own result.
    /// </remarks>
    public (LoopState First, LoopState Second)? TwoSendsInto(SyntaxNode creation)
    {
        if (!this.AlwaysCoalesces(creation) || this.coalescing[creation].InTheBlock is not { Count: 2 } sends)
        {
            return null;
        }

        KeyValuePair<SyntaxNode, LoopState>[] inOrder = [.. sends.OrderBy(static send => send.Key.SpanStart)];

        return (inOrder[0].Value, inOrder[1].Value);
    }

    /// <summary>
    ///     True when a sink has content here that it did not have in the given
    ///     read. A read that gives the sinks this content can then find more.
    /// </summary>
    public bool SaysMoreThan(SentContent before) =>
        this.toMade.Any(sent => sent.Value.Shape != before.To(sent.Key).Shape)
        || this.toNames.Any(sent => sent.Value.Shape != before.To(sent.Key).Shape)
        || this.coalescing.Keys.Any(creation =>
            this.CanCoalesce(creation) != before.CanCoalesce(creation)
            || this.AlwaysCoalesces(creation) != before.AlwaysCoalesces(creation)
            || !HaveOneShape(first: this.TwoSendsInto(creation), second: before.TwoSendsInto(creation)));

    // The two sends can change their own states and keep the state of their join. The function then
    // gets new values. A send of a value that a hold of a different sink makes is such a send, where a
    // send into that sink comes after it.
    private static bool HaveOneShape(
        (LoopState First, LoopState Second)? first,
        (LoopState First, LoopState Second)? second) =>
        first is { } sends
            ? second is { } others
              && sends.First.Shape == others.First.Shape
              && sends.Second.Shape == others.Second.Shape
            : second == null;
}
