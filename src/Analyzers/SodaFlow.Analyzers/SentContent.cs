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

    /// <summary>True when a send went into a sink that the walk did not see made.</summary>
    public bool HasNames => this.toNames.Count > 0;

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

    /// <summary>
    ///     True when a sink has content here that it did not have in the given
    ///     read. A read that gives the sinks this content can then find more.
    /// </summary>
    public bool SaysMoreThan(SentContent before) =>
        this.toMade.Any(sent => sent.Value.Shape != before.To(sent.Key).Shape)
        || this.toNames.Any(sent => sent.Value.Shape != before.To(sent.Key).Shape);
}
