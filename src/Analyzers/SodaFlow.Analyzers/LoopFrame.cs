using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    /// <summary>
    ///     For a local of this code whose collection keeps its positions: the
    ///     expression of each element, in order. Null for a local whose collection
    ///     does not. A local that no code asked about is not here.
    /// </summary>
    public Dictionary<ISymbol, IReadOnlyList<ExpressionSyntax>?> Positions { get; } =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local of this code that has one of the values of a collection:
    ///     the expression of that collection. The local is the variable of a
    ///     foreach, or a local that the code gave an element. It can also be a
    ///     parameter of a lambda that a method of the collection runs, or an
    ///     <c>out</c> variable.
    /// </summary>
    public Dictionary<ISymbol, ExpressionSyntax> ElementOf { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local of this code whose object a collection got: each such
    ///     collection. The collection is the expression that got the object, or
    ///     the declaration of a local whose literal has it. Thus, the local and an
    ///     element of the collection are two names for one object.
    /// </summary>
    public Dictionary<ISymbol, List<SyntaxNode>> Holders { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local of this code that a literal made: true when the collection
    ///     has one element and no code can change that number. A local that no
    ///     code asked about is not here.
    /// </summary>
    public Dictionary<ISymbol, bool> HasOneElement { get; } = new(SymbolEqualityComparer.Default);

    public void Wait(QueryValue query) => this.waiting.Add(query);

    /// <summary>
    ///     True after the walk of the code of this frame. A function that the
    ///     code made can run after that, when the caller kept it. Then, no walk
    ///     of this frame gets what the function finds.
    /// </summary>
    public bool HasReturned { get; private set; }

    public void Return() => this.HasReturned = true;
}
