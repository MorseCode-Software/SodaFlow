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
/// <param name="parent">The frame of the code that calls this code. Null for the block.</param>
/// <param name="repeats">The value of <see cref="Repeats" />.</param>
/// <param name="isOfAConstructor">The value of <see cref="IsOfAConstructor" />.</param>
internal sealed class LoopFrame(in LoopFrame? parent, in bool repeats, in bool isOfAConstructor)
{
    private readonly LoopFrame? parent = parent;
    private readonly List<QueryValue> waiting = [];

    /// <summary>
    ///     True when the walk reads the code of this frame one time for two or
    ///     more calls. The call is then in a loop, in a function in the code of
    ///     its caller, or in a frame for which this is true. An object that such
    ///     code makes is two or more objects at run time.
    /// </summary>
    public bool Repeats { get; } = repeats;

    /// <summary>
    ///     True when the code of this frame is a constructor. The object that the
    ///     code is in is then a new object. The receiver of a method can be older
    ///     than the call.
    /// </summary>
    public bool IsOfAConstructor { get; } = isOfAConstructor;

    /// <summary>
    ///     True for the given frame, and for the frame of each callee that the
    ///     code of the given frame calls.
    /// </summary>
    public bool IsWithin(LoopFrame other)
    {
        for (LoopFrame? frame = this; frame != null; frame = frame.parent)
        {
            if (frame == other)
            {
                return true;
            }
        }

        return false;
    }

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

    /// <summary>
    ///     The lambdas that a callee with no body to read runs one time for each
    ///     element, and that the walk reads where they are. A run for one element
    ///     is a turn, as a turn of a loop is.
    /// </summary>
    public HashSet<SyntaxNode> RunsForEachElement { get; } = [];

    /// <summary>
    ///     For a local that the body of a loop declares: the state that it had at
    ///     the end of the first read of the loop. A lambda or a local function of
    ///     the turn before reads that local, and not the local of this turn.
    /// </summary>
    public Dictionary<ISymbol, LoopState> OfTheTurnBefore { get; } = new(SymbolEqualityComparer.Default);

    public void Wait(QueryValue query) => this.waiting.Add(query);

    private readonly List<Finding> unread = [];

    /// <summary>
    ///     Records a member of an object that the code of this frame calls, and
    ///     that the walk does not read. The walk of the frame reports it with what
    ///     it found.
    /// </summary>
    public void DoesNotRead(Finding member) => this.unread.Add(member);

    /// <summary>The members from <see cref="DoesNotRead" />, each given one time.</summary>
    public IReadOnlyList<Finding> TakeUnread()
    {
        Finding[] all = [.. this.unread];
        this.unread.Clear();

        return all;
    }

    /// <summary>
    ///     True after the walk of the code of this frame. A function that the
    ///     code made can run after that, when the caller kept it. Then, no walk
    ///     of this frame gets what the function finds.
    /// </summary>
    public bool HasReturned { get; private set; }

    public void Return() => this.HasReturned = true;
}
