using System.Collections.Generic;
using System.Collections.Immutable;
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
    ///     For a local that is a second name of a value with no object of the
    ///     store: the expression of the first name. The expression is in the
    ///     assignment that gave the local that value.
    /// </summary>
    public Dictionary<ISymbol, ExpressionSyntax> FirstNameOf { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For the expression of each first name in <see cref="FirstNameOf" />:
    ///     the evaluator of the code that made the record. For a lambda or a
    ///     local function, that is one run of it.
    /// </summary>
    public Dictionary<ExpressionSyntax, LoopStateEvaluator> RunThatNamed { get; } = [];

    /// <summary>
    ///     Each name that was one of two names of a value with no object. The
    ///     walk does not know now that the two names are one value.
    /// </summary>
    public HashSet<ISymbol> WasOneOfTwoNames { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local that got a value with no object of the store from a
    ///     location: the expression of that location. The walk can read the
    ///     location again. Two locals with the same location are two names of
    ///     one value.
    /// </summary>
    public Dictionary<ISymbol, ExpressionSyntax> ReadFrom { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local whose value with no object of the store went into a
    ///     location: the expression of that location. The local is a name of
    ///     that location also, as a local of <see cref="ReadFrom" /> is.
    /// </summary>
    public Dictionary<ISymbol, ExpressionSyntax> GaveTo { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The objects that code with no body keeps, for an owner that is a
    ///     location that the walk can read again. The location is an element or a
    ///     field, through the local that is a name of it when the code has one. The
    ///     member is set for a field or a property of the value at that location.
    /// </summary>
    public List<(ExpressionSyntax Location, ISymbol? Name, ISymbol? Member, LoopState State)> KeptAt { get; } = [];

    /// <summary>
    ///     Each value that the code of this frame put into an element or into a
    ///     field, in order. The first part is the field, or null for an element.
    ///     The second part is the index of an element that has one index. Only
    ///     the frame of a callee that has a parameter with ref, out, or in keeps
    ///     them.
    /// </summary>
    public List<(ISymbol? Field, ArgumentSyntax? Index, LoopState Value)> PutsIntoLocations { get; } = [];

    /// <summary>
    ///     Each value that the code of this frame put into a location of the
    ///     object of a parameter, in order. The location is an element or a
    ///     field. The last part is the parameter. The caller gets each of them at
    ///     the end of the walk.
    /// </summary>
    public List<(ISymbol? Field, ArgumentSyntax? Index, LoopState Value, ISymbol Parameter)> PutsThroughParameters
    {
        get;
    } = [];

    /// <summary>
    ///     For a parameter with ref or out that this code gave a value: the
    ///     number of entries that <see cref="PutsIntoLocations" /> had at that
    ///     assignment.
    /// </summary>
    public Dictionary<ISymbol, int> PutsBeforeAnAssignment { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a parameter with ref or out: the value of
    ///     <see cref="ObjectStore.PositionChanges" /> at the last assignment to
    ///     it. The assignment always runs in this code.
    /// </summary>
    public Dictionary<ISymbol, int> PositionsAtASureAssignment { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>The frame of the block, which has no parent.</summary>
    public LoopFrame Root
    {
        get
        {
            LoopFrame frame = this;

            while (frame.parent != null)
            {
                frame = frame.parent;
            }

            return frame;
        }
    }

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
    ///     For a local of this code whose dictionary keeps the value of each
    ///     constant key of its initializer: the expression of each such value.
    ///     Null for a local whose dictionary does not. A local that no code asked
    ///     about is not here.
    /// </summary>
    public Dictionary<ISymbol, IReadOnlyDictionary<object, ExpressionSyntax>?> ValuesByKey { get; } =
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

    /// <summary>
    ///     For a local that is one collection and no other value: that
    ///     collection. The local got it from an assignment that always runs, and
    ///     no assignment after that gave the local a second value.
    /// </summary>
    public Dictionary<ISymbol, MadeObject> TheOneCollectionOf { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local with ref of this code: the expression of its location.
    ///     The location is a local, an element of an array in a local, or a
    ///     field of an object in a local.
    /// </summary>
    public Dictionary<ISymbol, ExpressionSyntax> LocationOf { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a loop of this code: each member that a write of its turn
    ///     replaced, with the state that the member had before the loop. The
    ///     write is to an object or through a name that is older than the turn.
    /// </summary>
    public List<(SyntaxNode Turn, MadeObject? Made, ISymbol? Name, ISymbol Member, LoopState Before)> BeforeATurn
    {
        get;
    } = [];

    /// <summary>
    ///     For a loop of this code: each collection that is older than the turn,
    ///     with its positions before the loop. An assignment of the turn replaced
    ///     an element of it.
    /// </summary>
    public List<(SyntaxNode Turn, MadeObject Made, ImmutableArray<LoopState> Before)> PositionsBeforeATurn
    {
        get;
    } = [];

    /// <summary>
    ///     For a local or a parameter of this code: each member that the code
    ///     set through it.
    /// </summary>
    public Dictionary<ISymbol, HashSet<ISymbol>> SetThrough { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The locals of this code whose object can be one that the store does
    ///     not know. Such a local got an element of a collection that did not
    ///     have the mark from <see cref="ObjectStore.IsComplete" />.
    /// </summary>
    public HashSet<ISymbol> CanBeUnseen { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For each foreach statement that the walk is in: the writes to a member
    ///     through its variable. The collection keeps its positions, or it has
    ///     the objects of a loop.
    /// </summary>
    public Dictionary<SyntaxNode, WritesThroughEach> WritesThroughEach { get; } = [];

    /// <summary>
    ///     For each <c>Add</c> of a list in a loop of this code: the list, and
    ///     the object of the turn that the call adds. Each turn always runs the
    ///     call. The object is null when a read of the loop added a different
    ///     value.
    /// </summary>
    public Dictionary<SyntaxNode, (MadeObject List, MadeObject? Added)> AddsOfEachTurn { get; } = [];

    /// <summary>
    ///     The state of each member of an element at a location that the walk
    ///     can read again. The last sure write through that location gave it, and
    ///     each write after that one that can be to the same object added to it.
    /// </summary>
    public List<ElementMemberWrite> ElementMemberWrites { get; } = [];

    /// <summary>
    ///     The number of writes to a member in the region when the code of this
    ///     frame last wrote to a member. A different number at a read means a write
    ///     that <see cref="ElementMemberWrites" /> does not have.
    /// </summary>
    public int MemberWritesSeen { get; set; }

    /// <summary>
    ///     The names that hold one object, each with the state of the members
    ///     that sure writes through the names gave.
    /// </summary>
    public List<NamesOfOneObject> NamesOfOneObject { get; } = [];

    /// <summary>
    ///     The number of writes to a member in the region when the code of this
    ///     frame last wrote to a member. A different number at a read means a write
    ///     that <see cref="NamesOfOneObject" /> does not have.
    /// </summary>
    public int NamesWritesSeen { get; set; }

    /// <summary>
    ///     The number of the writes to a member in the region, for each name of
    ///     this code. The number is from the last write through that name.
    /// </summary>
    public Dictionary<ISymbol, int> OwnWriteOf { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     Each local and each location of this code that got the object that the
    ///     code is in. The record has the function of the assignment and the end
    ///     of its value. A location has the key of LocationKeyOf.
    /// </summary>
    public List<(ISymbol? Local, (ISymbol Of, ISymbol? Owner, int Index)? Key, SyntaxNode Code, int From)>
        NamesOfThis { get; } = [];

    /// <summary>
    ///     The state of the last write to a member of this object, when that
    ///     write was through a name of <see cref="NamesOfThis" />. The member is
    ///     the origin of the state. A read through this or through a
    ///     field gets that origin, as the region gives it.
    /// </summary>
    public Dictionary<ISymbol, LoopState> WrittenThroughANameOfThis { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local of this code that got an element at a location that the
    ///     walk can read again: the expression of that element.
    /// </summary>
    public Dictionary<ISymbol, ElementAccessExpressionSyntax> NamesOfElements { get; } =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     For a local or a parameter: each use of it in the code of this frame.
    ///     A use has its position, true when it gives the name a value, and true
    ///     when a function in that code has it.
    /// </summary>
    public Dictionary<ISymbol, (SyntaxNode Code, ImmutableArray<(int Position, bool Gives, bool InAFunction)> Uses)>
        UsesOfNames { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The last write to a member through a name in the code of this frame,
    ///     when that write always runs. Null when the last write was not such a
    ///     write.
    /// </summary>
    public WriteThroughAName? LastWrite { get; set; }

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
