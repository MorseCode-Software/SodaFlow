using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Locations =
    System.Collections.Immutable.ImmutableDictionary<
        Microsoft.CodeAnalysis.ISymbol,
        (SodaFlow.Analyzers.LoopStateEvaluator Reader, Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Location)>;

namespace SodaFlow.Analyzers;

/// <summary>
///     Gives each expression in a loop region its <see cref="LoopState" />.
///     The rules are a model of what each library operator does with the current
///     value and the links of its inputs. A call to code of this compilation gets
///     its state from the body of that code. The evaluator walks that body as
///     the region walks the block, and gives the caller what the walk found.
/// </summary>
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     How many calls deep the evaluator follows a helper or a construction.
    ///     A sort two helpers down needs four. The two helpers are two calls, and
    ///     the member of an element and its own helper are two more. Six also
    ///     permits layers of helpers around that. A method that calls itself
    ///     usually stops before this limit, at <see cref="ShapeOfACallAgain" />.
    /// </summary>
    private const int MaxInlineDepth = 6;

    private static readonly ImmutableArray<string> Suffixes = ImmutableArray.Create(item1: "Impl", item2: "Internal");

    private static readonly ImmutableDictionary<ISymbol, LoopState> NoSymbols =
        ImmutableDictionary<ISymbol, LoopState>.Empty.WithComparers(SymbolEqualityComparer.Default);

    private static readonly ImmutableDictionary<ISymbol, ITypeSymbol> NoTypeArguments =
        ImmutableDictionary.Create<ISymbol, ITypeSymbol>(SymbolEqualityComparer.Default);

    private static readonly Locations NoLocations =
        ImmutableDictionary.Create<ISymbol, (LoopStateEvaluator Reader, ExpressionSyntax Location)>(
            SymbolEqualityComparer.Default);

    private static readonly ImmutableHashSet<ISymbol> NoParameters =
        ImmutableHashSet<ISymbol>.Empty.WithComparer(SymbolEqualityComparer.Default);

    // The locals and parameters that an assignment gave a new state, which replaced the first one.
    private readonly HashSet<ISymbol> assigned;
    private readonly ImmutableDictionary<ISymbol, LoopState> bound;

    // The parameters whose argument at this call is null or default.
    private readonly ImmutableHashSet<ISymbol> noValues;
    private readonly int depth;
    private readonly Dictionary<SyntaxNode, Followed?> followed = [];

    // The calls that the code writes with no call syntax. The key is the node and the member that
    // it calls. One node can make more than one such call, as an expression with an operator and
    // a conversion does.
    private readonly Dictionary<(SyntaxNode Node, ISymbol Member), Followed?> implied = new(ImpliedCalls.Instance);
    private readonly Dictionary<SyntaxNode, CalleeKind> kinds = [];
    private readonly Dictionary<ISymbol, LoopState> locals;

    // The most owners that a change to an element goes up to.
    private const int MaxOwners = 8;

    // The most locals in a chain that the evaluator goes through for the type of an object.
    private const int MaxKnownTypeDepth = 8;

    // The members that the platform calls with two objects.
    private const PlatformCallbacks ComparesTwo = PlatformCallbacks.Compare | PlatformCallbacks.Equals;

    private readonly Dictionary<ExpressionSyntax, LoopState> memo = [];

    // The state of an expression before the members of the store came in. Only a value that can be
    // an object that the store does not know has one. See Kept.
    private readonly Dictionary<ExpressionSyntax, LoopState> beforeTheStore = [];

    // The lambda or the local function that this evaluator reads for a call of it. Null for each
    // other code.
    private readonly SyntaxNode? function;

    private readonly Dictionary<ExpressionSyntax, LoopState> functionResults = [];

    // The call that this code runs for. Null for the code of the block.
    private readonly CallOfThisCode? call;

    // The objects that this code made for its call. Each of them is new in each call.
    private readonly HashSet<MadeObject> madeForTheCall = [];

    // The type of the object that a new expression makes. The walk of its constructor has it, and
    // so has each constructor that it calls first and each member that they call on this object.
    // A virtual member that one of them calls on this object is the member of that type. Null for
    // each other code.
    private INamedTypeSymbol? builtType;

    // The object that this code runs on, when the walk knows its type. It is the receiver of the call
    // of a method of it, as the caller read it. A call on this object runs the member of that type.
    // Such a call has no receiver, or its receiver is this. Null for each other code.
    private Construction? thisObject;

    // The code that defines the function that this evaluator reads, or null. See StateAtAPosition.
    private LoopStateEvaluator? definer;

    // The walker that reads the code of this evaluator. It knows where a call always runs.
    private SinkWalker? walker;

    // The queries that this code makes.
    private readonly Dictionary<SyntaxNode, QueryValue> queries = [];

    // True for an evaluator that reads code that does not run now, to learn what that code reads.
    // It has copies of the locals and the members, and it does not set a field of the region.
    private readonly bool quiet;
    private readonly LoopRegion region;
    private readonly Dictionary<ISymbol, LoopState> thisMembers;

    // The members of the object that this code set. The value is the one collection that the
    // member then is, when a write that always runs gave it. The code of a function in this code
    // has the same record, and so has a callee that is a member of the same object.
    private readonly Dictionary<ISymbol, MadeObject?> setOnThis;

    public LoopStateEvaluator(LoopRegion region)
        : this(
            region: region,
            bound: NoSymbols,
            noValues: NoParameters,
            depth: 0,
            frame: new LoopFrame(parent: null, repeats: false, isOfAConstructor: false),
            locals: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            assigned: new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            thisMembers: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            setOnThis: new Dictionary<ISymbol, MadeObject?>(SymbolEqualityComparer.Default),
            quiet: false,
            function: null,
            call: null)
    {
    }

    private LoopStateEvaluator(
        LoopRegion region,
        ImmutableDictionary<ISymbol, LoopState> bound,
        ImmutableHashSet<ISymbol> noValues,
        int depth,
        LoopFrame frame,
        Dictionary<ISymbol, LoopState> locals,
        HashSet<ISymbol> assigned,
        Dictionary<ISymbol, LoopState> thisMembers,
        Dictionary<ISymbol, MadeObject?> setOnThis,
        bool quiet,
        SyntaxNode? function,
        CallOfThisCode? call)
    {
        this.function = function;
        this.call = call;
        this.region = region;
        this.bound = bound;
        this.noValues = noValues;
        this.depth = depth;
        this.Frame = frame;
        this.locals = locals;
        this.assigned = assigned;
        this.thisMembers = thisMembers;
        this.setOnThis = setOnThis;
        this.quiet = quiet;
    }

    /// <summary>
    ///     An evaluator for the same code that changes nothing that the walk
    ///     keeps. It reads a function that does not run now, to learn if the
    ///     function reads a pending value.
    /// </summary>
    private LoopStateEvaluator Quiet() =>
        this.quiet
            ? this
            : new LoopStateEvaluator(
                region: this.region,
                bound: this.bound,
                noValues: this.noValues,
                depth: this.depth,
                frame: this.Frame,
                locals: new Dictionary<ISymbol, LoopState>(
                    dictionary: this.locals,
                    comparer: SymbolEqualityComparer.Default),
                assigned: new HashSet<ISymbol>(collection: this.assigned, comparer: SymbolEqualityComparer.Default),
                thisMembers: new Dictionary<ISymbol, LoopState>(
                    dictionary: this.thisMembers,
                    comparer: SymbolEqualityComparer.Default),
                setOnThis: new Dictionary<ISymbol, MadeObject?>(
                    dictionary: this.setOnThis,
                    comparer: SymbolEqualityComparer.Default),
                quiet: true,
                function: this.function,
                call: this.call) { walker = this.walker, builtType = this.builtType, thisObject = this.thisObject };

    /// <summary>The frame of the code that this evaluator reads.</summary>
    public LoopFrame Frame { get; }

    /// <summary>
    ///     Records a query for the frame of this code. The call or the query
    ///     expression makes that query. An evaluator that reads code which does
    ///     not run now records none. That query is in no state, thus a walk of
    ///     it changes no result.
    /// </summary>
    public void Wait(SyntaxNode call)
    {
        if (!this.quiet)
        {
            this.Frame.Wait(this.QueryAt(call));
        }
    }

    /// <summary>
    ///     Records a query for a read that has no expression of its own, and
    ///     gives the state with that query. A property pattern reads a property
    ///     whose getter is an iterator so.
    /// </summary>
    /// <param name="read">The node that makes the query. The walk reads the getter through it.</param>
    /// <param name="state">The state of what the getter returns.</param>
    public LoopState Waiting(SyntaxNode read, LoopState state)
    {
        this.Wait(read);

        return state.WithQuery(this.QueryAt(read));
    }

    /// <summary>The query that a call or a query expression of this code makes.</summary>
    private QueryValue QueryAt(SyntaxNode call)
    {
        if (!this.queries.TryGetValue(key: call, value: out QueryValue? query))
        {
            query = new QueryValue(call: call, definer: this);
            this.queries[call] = query;
        }

        return query;
    }

    /// <summary>
    ///     True for a call that makes a sequence and runs nothing: a query method
    ///     of the platform, or an iterator of this compilation. The code of the
    ///     function, of the key, or of the iterator runs when something enumerates
    ///     the result.
    /// </summary>
    public bool Waits(InvocationExpressionSyntax call, IMethodSymbol method) =>
        this.Classify(call: call, method: method) switch
        {
            CalleeKind.Mover => SodaFlowSymbols.MakesQuery(method),
            CalleeKind.Body => this.DefinitionsOf(invocation: call, method: method).All(IsIterator),
            _ => false
        };

    /// <summary>
    ///     True for a property whose getter is an iterator. A read of such a
    ///     property runs nothing, as a call of an iterator method runs nothing.
    /// </summary>
    public static bool IsIteratorProperty(IPropertySymbol property) =>
        property.OriginalDefinition.GetMethod is { } getter && IsIterator(getter);

    private static bool IsIterator(IMethodSymbol definition) =>
        (SodaFlowSymbols.IsEnumerated(definition.ReturnType)
         || (definition.IsAsync && SodaFlowSymbols.IsAsyncEnumerated(definition.ReturnType)))
        && BodyOf(definition) is BlockSyntax block
        && block.DescendantNodes(
                descendIntoChildren: static node =>
                    node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(static node => node is YieldStatementSyntax);

    /// <summary>
    ///     True when the code is a member of an object that has a member with a
    ///     state. Then, a name with no owner can be a property of that object.
    /// </summary>
    public bool HasThisMembers => this.thisMembers.Count > 0;

    /// <summary>
    ///     Records the state of a local at its declaration, or at an assignment
    ///     that does not always run. The local then has the two states together.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="from">The expression of the value, when the code has one.</param>
    public void Declare(ISymbol local, LoopState state, ExpressionSyntax? from = null)
    {
        state = this.InALocal(
            local: local,
            state: this.WithTheOtherNames(local: local, from: from, state: this.ForAName(state: state, from: from)));

        if (this.locals.TryGetValue(key: local, value: out LoopState? existing))
        {
            // The local can be the value that it had also.
            this.Frame.TheOneCollectionOf.Remove(local);
            this.locals[local] = existing.Join(state);
        }
        else
        {
            this.IsOneCollection(local: local, state: state, from: from);
            this.locals[local] = state;
        }
    }

    // The state that a name keeps for the value of an expression. A value that can be an object
    // that the store does not know has the members of that object. See Kept.
    private LoopState ForAName(LoopState state, ExpressionSyntax? from) =>
        from != null && state.Objects.Contains(MadeObject.Unknown) ? this.Kept(from) : state;

    // A collection gets a copy of a change to one of its objects. The local keeps the copy, and it
    // is the same value as before.
    private void KeepACopy(ISymbol local, LoopState state)
    {
        state = this.InALocal(local: local, state: state);

        this.locals[local] =
            this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing.Join(state) : state;

        this.NotesAChangeTo(local);
    }

    // Records the collection of a local that has one value. The value is one new object of the
    // store, or it is a second local that is one collection. A local with no expression for its
    // value is no such local. The variable of a foreach is one element of a collection. That
    // collection can hold an object that the walk did not see go in. A name with ref is its
    // location, which can get a second value with no assignment to the name.
    private void IsOneCollection(ISymbol local, LoopState state, ExpressionSyntax? from)
    {
        if (from == null || !KeepsItsObject(local))
        {
            this.Frame.TheOneCollectionOf.Remove(local);
        }
        else if (Unwrapped(from) is IdentifierNameSyntax name
                 && this.region.SymbolOf(name) is ILocalSymbol other
                 && this.Frame.TheOneCollectionOf.TryGetValue(key: other, value: out MadeObject? same))
        {
            this.Frame.TheOneCollectionOf[local] = same;
        }
        else if (TheOneNewObject(state) is { } only)
        {
            this.Frame.TheOneCollectionOf[local] = only;

            // Only a collection with the mark needs the answer, which reads each use of the local.
            if (this.region.Objects.HoldsOnlyWhatWentIn(only) && this.IsTheOnlyNameOfItsObject(local))
            {
                this.region.Objects.IsApart(only);
            }
        }
        else
        {
            this.Frame.TheOneCollectionOf.Remove(local);
        }
    }

    // True for a local whose object no other code can get to. Each use of the local in its block is
    // a read, or a change of only its list. Any other use can give the object a second name. A
    // function can go to code that the walk cannot read, thus no function uses the local.
    private bool IsTheOnlyNameOfItsObject(ISymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarator
        && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } scope
        && scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(use => use.Identifier.ValueText == local.Name
                          && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: local))
            .All(use => FunctionOf(use) == FunctionOf(declarator) && (this.IsARead(use) || this.ChangesOnlyItsList(use)));

    // The name is the owner in a call of a method of a list of the platform. The method puts a
    // value in or takes one out, and gives the list to no code.
    private bool ChangesOnlyItsList(SyntaxNode use) =>
        use.Parent is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "Add" or "Insert" or "Remove" or "RemoveAt" or "Clear",
            Parent: InvocationExpressionSyntax call
        } access
        && access.Expression == use
        && this.region.SymbolOf(call) is IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" } owner }
        && SodaFlowSymbols.IsPlatform(owner);

    // The object of a value that is one new object of the store, and no object of a turn before.
    private static MadeObject? TheOneNewObject(LoopState state)
    {
        MadeObject? only = state.Objects.Length == 1 ? state.Objects[0] : null;

        return only is { IsNew: true, IsOfTurnsBefore: false } ? only : null;
    }

    /// <summary>
    ///     Records an assignment that always runs. The new state replaces the
    ///     state that the local or the parameter had.
    /// </summary>
    /// <param name="local">The local or the parameter.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="from">The expression of the value, when the code has one.</param>
    /// <param name="kept">
    ///     The expression of the value, when the state is the state of that
    ///     expression. The local then keeps the state that <see cref="ForAName" />
    ///     gives.
    /// </param>
    public void Assign(ISymbol local, LoopState state, ExpressionSyntax? from = null, ExpressionSyntax? kept = null)
    {
        state = this.InALocal(
            local: local,
            state: this.WithTheOtherNames(local: local, from: kept, state: this.ForAName(state: state, from: kept)));

        // A function can run two or more times, and at a different time. Thus, an assignment in a
        // function makes a local no one collection.
        this.IsOneCollection(local: local, state: state, from: this.function == null ? from : null);
        this.locals[local] = state;
        this.assigned.Add(local);

        // The parameter is the value of this assignment until this code puts a value into a
        // location after it.
        if (local is IParameterSymbol { RefKind: not RefKind.None })
        {
            this.Frame.PutsBeforeAnAssignment[local] = this.Frame.PutsIntoLocations.Count;

            // A function can run at a different time from its code.
            if (this.function == null)
            {
                this.Frame.PositionsAtASureAssignment[local] = this.region.Objects.PositionChanges;
            }
        }
    }

    /// <summary>
    ///     Records an assignment that each turn of a loop always runs, to a local
    ///     that is older than the turn. Until the end of the turn, the local has
    ///     only the new value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A loop can run no turn, and its last turn can end before the
    ///         assignment. Thus, the caller keeps the state that the local had, and
    ///         gives it back with <see cref="Declare" /> after the loop.
    ///     </para>
    ///     <para>
    ///         An object of the turn in the new value is only that object, and not
    ///         one of the turns before. No code of an older turn runs before the end
    ///         of this turn. At the end of the turn, the object becomes one of the
    ///         objects of the turns before, in each location that has it.
    ///     </para>
    /// </remarks>
    /// <param name="local">The local.</param>
    /// <param name="state">The state of the new value.</param>
    /// <returns>The state that the local had. None for a local with no state.</returns>
    public LoopState AssignInATurn(ISymbol local, LoopState state)
    {
        LoopState had = this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing : LoopState.None;
        this.Frame.TheOneCollectionOf.Remove(local);
        this.locals[local] = state;

        return had;
    }

    /// <summary>
    ///     True for a local that the given code declares. A local that the body of
    ///     a loop declares is new in each turn.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="code">The body of a loop, or a function.</param>
    public static bool IsDeclaredIn(ISymbol local, SyntaxNode code) =>
        IsDeclaredIn(declarations: local.DeclaringSyntaxReferences, code: code);

    /// <summary>True when the given code has one of the declarations.</summary>
    /// <param name="declarations">The declarations of a local.</param>
    /// <param name="code">The body of a loop, or a function.</param>
    public static bool IsDeclaredIn(ImmutableArray<SyntaxReference> declarations, SyntaxNode code) =>
        declarations.Any(reference => reference.SyntaxTree == code.SyntaxTree && code.Span.Contains(reference.Span));

    /// <summary>The body of a loop statement. Null for a node that is not a loop statement.</summary>
    public static StatementSyntax? LoopBodyOf(SyntaxNode node) =>
        node switch
        {
            ForStatementSyntax loop => loop.Statement,
            CommonForEachStatementSyntax loop => loop.Statement,
            WhileStatementSyntax loop => loop.Statement,
            DoStatementSyntax loop => loop.Statement,
            _ => null
        };

    /// <summary>
    ///     Records the end of the first read of a loop, before the walk reads its
    ///     turn a second time. The second read is the second turn.
    /// </summary>
    /// <remarks>
    ///     A location that is older than the turn keeps what the first read gave
    ///     it. The second read finds a read of that value. Each object of the
    ///     first read is then one of the objects of the turns before. The evaluator
    ///     reads each expression of the loop again, because its state can be a
    ///     different one now. A local that the body declares is new in each turn,
    ///     thus it has no state from the first read.
    /// </remarks>
    /// <param name="loop">The loop statement.</param>
    /// <param name="turn">The body of the loop.</param>
    public void StartTheSecondTurn(SyntaxNode loop, SyntaxNode turn)
    {
        this.EndTurns(turn);
        Forget(found: this.memo, code: loop, node: static key => key);
        Forget(found: this.followed, code: loop, node: static key => key);
        Forget(found: this.implied, code: loop, node: static key => key.Node);

        foreach (ISymbol local in this.locals.Keys
                     .Where(local => this.region.IsDeclaredIn(local: local, code: turn))
                     .ToArray())
        {
            this.Frame.OfTheTurnBefore[local] = this.locals[local];
            this.locals.Remove(local);
        }
    }

    /// <summary>
    ///     A number for what the next turn of a loop can find. Its parts are each
    ///     local that is older than the turn, each member of this object, each
    ///     object of the store, and each field. A read that does not change it gave
    ///     no location a new state.
    /// </summary>
    /// <param name="turn">The body of the loop.</param>
    public int ShapeOfWhatALoopLeaves(SyntaxNode turn) =>
        unchecked(
            Shapes(this.locals.Where(local => !this.region.IsDeclaredIn(local: local.Key, code: turn)))
            + Shapes(this.thisMembers) * 3
            + this.region.Objects.Shape * 5
            + this.region.ShapeOfFields * 7
            + this.Frame.TheOneCollectionOf.Count * 11);

    private static int Shapes(IEnumerable<KeyValuePair<ISymbol, LoopState>> states) =>
        states.Aggregate(
            seed: 0,
            func: static (sum, one) => unchecked(
                sum + ShapeNumber.Of(location: ShapeNumber.Of(one.Key.Name), state: one.Value.Shape)));

    private static void Forget<TKey, TValue>(Dictionary<TKey, TValue> found, SyntaxNode code, Func<TKey, SyntaxNode> node)
        where TKey : notnull
    {
        foreach (TKey key in found.Keys.Where(key => IsIn(node: node(key), code: code)).ToArray())
        {
            found.Remove(key);
        }
    }

    /// <summary>True for a node that is a part of the given code.</summary>
    public static bool IsIn(SyntaxNode node, SyntaxNode code) =>
        node.SyntaxTree == code.SyntaxTree && code.Span.Contains(node.Span);

    /// <summary>
    ///     Records the end of the walk of a loop. Each object of its turn is then
    ///     one of the objects of the turns before.
    /// </summary>
    /// <param name="turn">The body of the loop.</param>
    public void EndTurns(SyntaxNode turn)
    {
        if (!this.quiet)
        {
            this.region.Objects.EndTurns(turn);
        }
    }

    /// <summary>
    ///     The state of a value in a location that can be older than a turn. Such
    ///     a location can have the object of a turn before. Thus, where the value
    ///     is the object of a turn, it can be one of the objects of the turns
    ///     before also.
    /// </summary>
    /// <param name="state">The state of the value.</param>
    /// <param name="location">The location, as the function needs it.</param>
    /// <param name="isOfItsTurn">
    ///     True when the location is new in each turn of the given loop, which is
    ///     the loop of the given object.
    /// </param>
    private LoopState InALocation<TLocation>(
        LoopState state,
        TLocation location,
        Func<SyntaxNode, MadeObject, TLocation, bool> isOfItsTurn) =>
        this.quiet || !this.region.Objects.HasATurn
            ? state
            : this.WithTheObjectsBefore(state: state, location: location, isOfItsTurn: isOfItsTurn);

    // This code is not in InALocation, because only this code makes a closure. Most calls of
    // InALocation do not come here.
    private LoopState WithTheObjectsBefore<TLocation>(
        LoopState state,
        TLocation location,
        Func<SyntaxNode, MadeObject, TLocation, bool> isOfItsTurn) =>
        // The identity before can be of a turn of a loop around. A location that is older than
        // that loop also gets the next identity only from a read before. The first read of the
        // loop around is its first turn, which has no turn before.
        state.WithAlso(made =>
            made.Turn is { } turn && !isOfItsTurn(arg1: turn, arg2: made, arg3: location)
                ? this.region.Objects.BeforeOf(made)
                : null);

    // A local is new in each turn of the loop that declares it in its body. A local of a callee
    // that runs in the turn is new in each turn also.
    private LoopState InALocal(ISymbol local, LoopState state) =>
        this.InALocation(
            state: state,
            location: (Code: this, Local: local),
            isOfItsTurn: static (turn, made, location) =>
                location.Code.RunsInTheTurnOf(made)
                || IsDeclaredIn(local: location.Local, code: turn)
                || location.Code.IsNewInEachTurnOf(local: location.Local, turn: turn));

    // A location that is new in no turn.
    private LoopState InAnOldLocation(LoopState state) =>
        this.InALocation(state: state, location: false, isOfItsTurn: static (_, _, isNew) => isNew);

    // A local of a callee is new in each call. A local of a lambda or a local function that the
    // walk reads for a call is new in each call also. When each turn of a loop runs the call one
    // time at most, the local is new in each turn of that loop.
    private bool IsNewInEachTurnOf(ISymbol local, SyntaxNode turn) =>
        this.call?.Turn == turn
        && (this.function == null || IsDeclaredIn(local: local, code: this.function));

    // The identity of the code that this evaluator reads for its call. The block is its own.
    private object CodeOfThisCall => this.call?.Code ?? this.Frame;

    // True when a call runs a function that this run of the code made. The call names a local
    // function, or it names a local whose declaration has the lambda and that gets no second
    // value. A method of the platform that runs a function in the call can get such a name as an
    // argument also. The code of the call must be of this run: the block, or such a function.
    private bool RunsAFunctionOfThisRun(SyntaxNode call, SyntaxNode function)
    {
        if ((this.function != null && this.call is not { RunsAFunctionOfThisRun: true })
            || call is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        return this.IsAFunctionOfThisRun(name: invocation.Expression, function: function)
               || (this.region.SymbolOf(invocation) is IMethodSymbol { ContainingType: { } owner }
                   && SodaFlowSymbols.IsPlatform(owner)
                   && invocation.ArgumentList.Arguments.Any(argument =>
                       this.IsAFunctionOfThisRun(name: argument.Expression, function: function)));
    }

    private bool IsAFunctionOfThisRun(ExpressionSyntax name, SyntaxNode function)
    {
        ExpressionSyntax callee = Unwrapped(name);

        // Only a simple name can name a local or a local function. The symbol of a different name
        // can cost a full bind of its chain of owners.
        if (callee is not SimpleNameSyntax)
        {
            return false;
        }

        return this.region.SymbolOf(callee) switch
        {
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } named =>
                named.DeclaringSyntaxReferences.Any(declaration => declaration.GetSyntax() == function),
            ILocalSymbol local =>
                local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                && Unwrapped(made) == function
                && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } scope
                && !this.GetsASecondValue(local: local, scope: scope),
            _ => false
        };
    }

    // True when code of the scope can give a local a value after its declaration. That code is an
    // assignment to the local, or an argument with ref or out.
    private bool GetsASecondValue(ISymbol local, SyntaxNode scope) =>
        scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(use =>
                use.Identifier.ValueText == local.Name
                && ((use.Parent is AssignmentExpressionSyntax assignment && assignment.Left == use)
                    || use.Parent is ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None })
                && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: local));

    // The bodies of the loops that an expression of this code is in, the nearest first.
    private ImmutableArray<SyntaxNode> LoopsOf(SyntaxNode place)
    {
        if (this.TurnOf(place) is { } nearest)
        {
            return this.LoopsAround(nearest).Insert(index: 0, item: nearest);
        }

        return this.call is { Turn: { } ofTheCall } && this.RunsOneTimeInThisCode(place)
            ? this.call.LoopsAroundTheTurn.Insert(index: 0, item: ofTheCall)
            : ImmutableArray<SyntaxNode>.Empty;
    }

    // The body of the loop whose each turn runs a call one time at most. The call can be in the
    // body of that loop. It can also run one time for each run of this code, where each turn of a
    // loop runs this code one time at most.
    private SyntaxNode? TurnOfACall(SyntaxNode call) =>
        this.TurnOf(call) ?? (this.RunsOneTimeInThisCode(call) ? this.call?.Turn : null);

    // True when an expression runs one time at most for each run of the code that this evaluator
    // reads. That code is a callee, or a function that the walk reads for a call.
    private bool RunsOneTimeInThisCode(SyntaxNode place)
    {
        foreach (SyntaxNode ancestor in place.Ancestors())
        {
            if (ancestor == this.function || ancestor == this.region.Root)
            {
                return true;
            }

            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda when this.RunsOneTimeInItsCall(lambda):
                    continue;
                case ForStatementSyntax
                    or CommonForEachStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
                    or AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or QueryExpressionSyntax:
                    return false;
                case MemberDeclarationSyntax or AccessorDeclarationSyntax:
                    return true;
            }
        }

        return false;
    }

    // True for a lambda that the code writes at a call of the library which runs its function one
    // time, as a transaction does. The code of the lambda then runs one time for each run of the
    // call, as the code around the call does.
    private bool RunsOneTimeInItsCall(SyntaxNode lambda) =>
        lambda.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call }
        && this.region.SymbolOf(call) is IMethodSymbol method
        && this.region.Symbols.IsSodaFlow(method)
        && OperatorTable.RunsFunctionOneTime(method);

    /// <summary>
    ///     Records the walker that reads the code of this evaluator. The evaluator
    ///     asks it where a call always runs, at the time when it follows the call.
    /// </summary>
    /// <returns>The walker.</returns>
    public SinkWalker ReadBy(SinkWalker reader)
    {
        this.walker = reader;

        return reader;
    }

    // Where a call always runs. The first part is true when each run of the block runs the call.
    // The second part is the body of the loop whose each turn always runs the call, or null when
    // no loop does. A call that always runs in its function has what the call of that function has.
    private (bool Always, SyntaxNode? Turn) WhereItRuns(SyntaxNode call)
    {
        (bool inItsFunction, SyntaxNode? turn) = this.walker?.WhereACallRuns(call) ?? (false, null);

        return (inItsFunction && (this.call?.AlwaysRuns ?? true),
            turn ?? (inItsFunction ? this.call?.AlwaysTurn : null));
    }

    // The frame of the code that has the loop of the second part of WhereItRuns. A loop of this
    // code is in this frame. A call that always runs in its function has what the call of that
    // function has.
    private LoopFrame? FrameOfTheTurnOf(SyntaxNode call)
    {
        (bool inItsFunction, SyntaxNode? turn) = this.walker?.WhereACallRuns(call) ?? (false, null);

        return turn != null ? this.Frame : inItsFunction ? this.call?.FrameOfTheAlwaysTurn : null;
    }

    // True when a change of this code to one object is sure to be the last state of that object.
    // The change always runs in its function when the second argument is true. The third argument
    // is the body of the loop whose each turn always runs the change. See Change.
    private bool IsSureFor(MadeObject made, bool replaces, SyntaxNode? turn) =>
        !made.IsOfTurnsBefore
        && (made.IsFrom(this.Frame)
            ? made.Turn == null
                ? replaces && this.AlwaysRunsFor(made)
                : made.Turn == (turn ?? (replaces ? this.TurnOfAChangeTo(made) : null))
            : replaces && this.AlwaysRunsForItsCaller(made));

    // A write in a lambda or a local function always runs for the block only when the call of the
    // function does. That is not a condition for an object that the function made for the call. A
    // callee with a frame of its own replaces the state of only its own objects.
    private bool AlwaysRunsFor(MadeObject made) =>
        this.function == null || this.madeForTheCall.Contains(made) || this.call is { AlwaysRuns: true };

    // A write in a callee to an object that the callee did not make. The write always runs in the
    // callee. It always runs for the block when each run of the block runs the call one time. The
    // object must be one object, and no object of a turn. A lambda or a local function of the
    // callee can run at a different time.
    private bool AlwaysRunsForItsCaller(MadeObject made) =>
        made.Turn == null
        && this.function == null
        && !this.Frame.Repeats
        && this.call is { AlwaysRuns: true, RunsOneTime: true };

    // The body of the loop of a caller whose each turn always runs the call of this code. The
    // second part is the frame of the code that has the loop. A write of this code that always
    // runs replaces until the end of that turn. For an object that is older than the turn, the
    // loop then gives back what the member had before. The object of the turn itself is new in
    // the next turn, and no code reads that object after the loop. Only a loop statement gives a
    // call such a turn.
    private (SyntaxNode Turn, LoopFrame Frame)? TurnOfACaller =>
        this.function == null && this.call is { AlwaysTurn: { } turn, FrameOfTheAlwaysTurn: { } frame }
            ? (turn, frame)
            : null;

    // The body of the loop whose each turn always runs a change of this code to an object. The
    // change is a statement that always runs in its function, with no loop of that function
    // around it. For an object that this code made for its call, that is the loop of the call.
    // For each other object in a lambda or a local function, the call must always run in the turn.
    private SyntaxNode? TurnOfAChangeTo(MadeObject made) =>
        this.call == null
            ? null
            : this.function == null || this.madeForTheCall.Contains(made)
                ? this.call.Turn
                : this.call.AlwaysTurn;

    // True when the code of this frame runs in one turn of the loop that made the object. The
    // frame is then of a callee of the code that has the loop, and the call did not return. A
    // function that a callee made can run in a turn after that one, with the locals of the callee.
    private bool RunsInTheTurnOf(MadeObject made) => !made.IsFrom(this.Frame) && !this.Frame.HasReturned;

    // A member or an element of an object is new in each turn of the loop that made the object.
    // It is new in each turn of each loop around that loop also. That is true only for a value
    // that is one object. The object must be a new one: the result of a call can be the same
    // object in each turn.
    private LoopState InAnObject(LoopState owner, LoopState state) =>
        this.InAnObjectOf(turn: TurnOfTheNewOne(owner), state: state);

    // The same, for an object that the given loop makes in each turn. Null for an object that no
    // turn makes.
    private LoopState InAnObjectOf(SyntaxNode? turn, LoopState state) =>
        this.InALocation(
            state: state,
            location: (Store: this.region.Objects, Turn: turn),
            isOfItsTurn: static (turn, _, location) => location.Store.IsIn(turn: location.Turn, around: turn));

    // The turn of the one object of a value, when that object is a new one.
    private static SyntaxNode? TurnOfTheNewOne(LoopState value)
    {
        if (value.Objects.Length != 1)
        {
            return null;
        }

        MadeObject one = value.Objects[0];

        return one.IsNew ? one.Turn : null;
    }

    /// <summary>
    ///     Records that a local has one of the values that a collection contains.
    ///     A change to the object in the local is then a change to what the
    ///     collection contains.
    /// </summary>
    public void HoldsOneOf(ISymbol local, ExpressionSyntax collection)
    {
        if (!HasACopy(local))
        {
            this.Frame.ElementOf[local] = collection;
        }

        if (!this.HoldsOnlyWhatWentIn(collection))
        {
            this.Frame.CanBeUnseen.Add(local);
        }
    }

    /// <summary>
    ///     Records the value that a local gets. A value that is an element of a
    ///     collection makes the local a second name for that element.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the local then has only this value.</param>
    public void HoldsValueOf(ISymbol local, ExpressionSyntax value, bool replaces)
    {
        // The collections that got the object of the local do not have its new object.
        if (replaces)
        {
            this.Frame.Holders.Remove(local);
            this.Frame.CanBeUnseen.Remove(local);
        }

        this.NotesASecondName(local: local, value: value, replaces: replaces);
        this.NotesANameOfALocation(local: local, value: value, replaces: replaces);
        this.NotesANameOfAnElement(local: local, value: value);

        if (KeepsItsObject(local))
        {
            this.NotesALink(name: new NameKey(of: local, owner: null, index: -1), value: value, replaces: replaces);
        }

        this.NotesANameOfThis(local: local, location: null, value: value, replaces: replaces);

        // A name with ref gets its location here. Each other assignment to the name is an
        // assignment to that location, and the name keeps it.
        if (value is RefExpressionSyntax { Expression: var location })
        {
            if (replaces
                && local is ILocalSymbol
                && (this.IsALocationThatTheWalkCanReadAgain(location)
                    || this.IsAStaticField(location)
                    || this.IsARefIndexerOfALocal(location)
                    || this.IsAFieldOfAnElementOfAStaticArray(location)))
            {
                this.Frame.LocationOf[local] = location;
            }
            else
            {
                this.Frame.LocationOf.Remove(local);
            }
        }

        // The value says if its object can be one that the store does not know. An element at a
        // position that the walk knows is the object of that position.
        if (this.CanBeUnseen(value))
        {
            this.Frame.CanBeUnseen.Add(local);
        }

        if (CopiesIntoItsLocation(name: local, value: value))
        {
            return;
        }

        if (this.CollectionOf(value) is { } collection && !HasACopy(local))
        {
            this.Frame.ElementOf[local] = collection;
        }
        else if (replaces && (value is RefExpressionSyntax || local is not ILocalSymbol { RefKind: not RefKind.None }))
        {
            // A name with ref that gets a value with no ref keeps its location, and the element
            // of that location gets the value.
            this.Frame.ElementOf.Remove(local);
        }
    }

    /// <summary>
    ///     Records that a collection got a value. The value can be the object of
    ///     a local. That object then has two names, and a change through the local
    ///     is a change to what the collection contains. A literal gives the
    ///     collection each of its elements.
    /// </summary>
    /// <param name="collection">
    ///     The expression of the collection, or the declaration of the local that
    ///     a literal is for.
    /// </param>
    /// <param name="value">The expression of the value.</param>
    public void Gets(SyntaxNode collection, ExpressionSyntax value)
    {
        // The collection holds the object of the value. Only a put does that: a call that can
        // keep its argument, as Contains is for the analyzer, makes no second name.
        if (collection is ExpressionSyntax written)
        {
            LoopState put = this.Eval(value);

            // A value that the store does not know comes in.
            if (!this.IsKnownWhenItGoesIn(value))
            {
                this.CanHoldMore(written);
            }

            // A value with members that is no object of the store has those members only in the
            // copy that the collection gets. Thus, the collection then holds an object that the
            // store does not know.
            this.Change(
                owner: this.Eval(written),
                change: old => put.IsOfNoObject
                    ? old.HoldingWhatCameWith(put).HoldingAnUnknownObject
                    : old.HoldingWhatCameWith(put),
                replaces: false,
                turn: null);
        }

        if (Unwrapped(value) is IdentifierNameSyntax name
            && this.region.SymbolOf(name) is { } local and (ILocalSymbol or IParameterSymbol))
        {
            if (!this.Frame.Holders.TryGetValue(key: local, value: out List<SyntaxNode>? holders))
            {
                holders = [];
                this.Frame.Holders[local] = holders;
            }

            if (!holders.Contains(collection))
            {
                holders.Add(collection);
            }

            return;
        }

        this.GetsElementsOf(collection: collection, literal: value);
    }

    /// <summary>
    ///     Records that a collection got each element of a literal. An expression
    ///     that is not a literal gives nothing here.
    /// </summary>
    public void GetsElementsOf(SyntaxNode collection, ExpressionSyntax literal)
    {
        IEnumerable<ExpressionSyntax> elements =
            Unwrapped(literal) switch
            {
                CollectionExpressionSyntax written =>
                    written.Elements.OfType<ExpressionElementSyntax>().Select(static element => element.Expression),
                // An initializer with a name gives a member a value, and puts nothing into a
                // collection. An initializer with an index puts its value there.
                InitializerExpressionSyntax initializer =>
                    initializer.Expressions
                        .Where(static element =>
                            element is not AssignmentExpressionSyntax { Left: not ImplicitElementAccessSyntax })
                        .Select(static element =>
                            element is AssignmentExpressionSyntax indexed ? indexed.Right : element),
                ArrayCreationExpressionSyntax { Initializer: { } initializer } => [initializer],
                ImplicitArrayCreationExpressionSyntax implied => [implied.Initializer],
                ObjectCreationExpressionSyntax { Initializer: { } initializer } => [initializer],
                ImplicitObjectCreationExpressionSyntax { Initializer: { } initializer } => [initializer],
                _ => []
            };

        foreach (ExpressionSyntax element in elements)
        {
            this.Gets(collection: collection, value: element);
        }
    }

    private void SetField(ISymbol field, LoopState state, bool replaces = false)
    {
        if (!this.quiet)
        {
            // The owner of the field is not known, thus it is new in no turn.
            this.region.SetField(field: field, state: this.InAnOldLocation(state), replaces: replaces);
        }
    }

    /// <summary>
    ///     Adds a state to what a name holds, for a value that a call puts into
    ///     the object that the name is for. <c>list.Add(value)</c> is such a call.
    /// </summary>
    public void Absorb(ExpressionSyntax target, LoopState state) =>
        // The object contains the value. It is not the value. A put gives the object what it
        // holds, and Gets records that.
        this.Merge(target: target, state: state.AsContainer.WithNoObjects);

    /// <summary>
    ///     Adds a state to what a name holds, as it is. A callee that put a
    ///     value into the object of a parameter gives that state to the argument.
    /// </summary>
    public void Merge(ExpressionSyntax target, LoopState state) => this.Merge(target: target, state: state, level: 0);

    private void Merge(ExpressionSyntax target, LoopState state, int level)
    {
        if (state.IsNone)
        {
            return;
        }

        // A change to an element of a collection is a change to what the collection contains.
        // The element is then not the one that the literal made. Thus, the collection does not
        // keep its positions. The collection can be an element also, and the walk goes up to
        // each owner. The number of owners has an end, because a local can name its own owner.
        // The flag is true when the value that changed last is only objects of the store. The
        // store then has the change for each of them, and a collection that holds them keeps its
        // positions.
        bool isInTheStore = false;

        for (; level < MaxOwners; level++)
        {
            // Parentheses, a cast, and the operator that says not null do not hide the name.
            target = Unwrapped(target);
            LoopState owner = this.Eval(target);
            state = this.InAnObject(owner: owner, state: state);
            LoopState added = state;

            (MadeObject Made, ImmutableArray<LoopState> All)? kept =
                isInTheStore ? this.StorePositionsOf(target) : null;

            this.Change(owner: owner, change: old => old.Join(added), replaces: false, turn: null);

            if (kept is (var made, var all))
            {
                this.region.Objects.HasPositions(collection: made, values: all);
            }

            // The owners of this value get the change next.
            isInTheStore = !owner.Objects.IsEmpty && !owner.Objects.Contains(MadeObject.Unknown);

            ExpressionSyntax? collection = this.CollectionOf(target);

            // An index has no name of its own to keep a state.
            if (collection == null || target is not ElementAccessExpressionSyntax)
            {
                this.MergeInto(target: target, state: state, level: level);
            }

            // The object of a local can be in a collection that got it from that local.
            if (target is IdentifierNameSyntax name
                && this.region.SymbolOf(name) is { } held and (ILocalSymbol or IParameterSymbol))
            {
                this.MergeIntoHolders(
                    local: held,
                    state: AsACopyOfAChangeTo(changed: owner, state: state),
                    level: level + 1);
            }

            if (collection == null)
            {
                return;
            }

            if (this.region.SymbolOf(collection) is ILocalSymbol local)
            {
                this.Frame.Positions[local] = null;
                this.Frame.ValuesByKey[local] = null;
            }

            // A collection can contain an object that the walk did not see go in, and the element
            // can be that object. The copy is then the only record of the change. Thus, the
            // collection holds an object that the store does not know from this change on. A
            // collection that holds only objects that the walk saw go in has no such object.
            state =
                this.HoldsOnlyWhatWentIn(collection)
                    ? state.AsContainer
                    : state.AsContainer.HoldingAnUnknownObject;

            target = collection;
        }
    }

    // A collection gets a copy of each change to the object of a local that it got. For a value
    // that is an object of the store, the store has the change also. For a value with no object,
    // the copy is the only record of the change. Thus, the collection then holds an object that
    // the store does not know, and an element of it keeps the copy.
    private static LoopState AsACopyOfAChangeTo(LoopState changed, LoopState state) =>
        changed.Objects.IsEmpty ? state.AsContainer.HoldingAnUnknownObject : state.AsContainer;

    // Each collection that got the object of a local contains the change to that object.
    private void MergeIntoHolders(ISymbol local, LoopState state, int level)
    {
        if (level >= MaxOwners || !this.Frame.Holders.TryGetValue(key: local, value: out List<SyntaxNode>? holders))
        {
            return;
        }

        foreach (SyntaxNode holder in holders.ToArray())
        {
            // A collection in an expression got the object in a call or in an assignment. Such a
            // use is not a read, thus the collection has no positions.
            if (holder is ExpressionSyntax written)
            {
                this.Merge(target: written, state: state, level: level);
            }
            else if (this.region.ModelFor(holder).GetDeclaredSymbol(holder) is ILocalSymbol declared)
            {
                // A literal in a declaration. The local has no expression there.
                this.Frame.Positions[declared] = null;
                this.Frame.ValuesByKey[declared] = null;
                this.KeepACopy(local: declared, state: state);
                this.MergeIntoHolders(local: declared, state: state.AsContainer, level: level + 1);
            }
        }
    }

    private void MergeInto(SyntaxNode target, LoopState state, int level)
    {
        switch (this.region.SymbolOf(target))
        {
            case { } local and (ILocalSymbol or IParameterSymbol) when target is IdentifierNameSyntax:
                this.KeepACopy(local: local, state: state);
                break;
            case { IsStatic: false } member and (IFieldSymbol or IPropertySymbol)
                when target is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                this.SetMember(member: member, state: state);
                break;
            case { } field and (IFieldSymbol or IPropertySymbol):
                this.SetField(field: field, state: state);

                // A member of an object that a name is for, as in box.Items. An object that the
                // evaluator saw made keeps a state for each member, and a read of the member
                // through the object has that state. Thus, the object gets the state for the
                // member also.
                if (target is MemberAccessExpressionSyntax { Expression: var owner })
                {
                    this.Merge(
                        target: owner,
                        state: LoopState.None.WithMembers(Construction.Empty.With(member: field, state: state)),
                        level: level + 1);
                }

                break;
        }
    }

    public LoopState Eval(ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return LoopState.None;
        }

        if (this.memo.TryGetValue(key: expression, value: out LoopState? known))
        {
            return known;
        }

        this.region.StopWhenCanceled();

        // The evaluator can go in a cycle only through a reassignment, and none is the correct
        // answer for the first visit.
        this.memo[expression] = LoopState.None;

        // The walk reads a node one time, and the value keeps the state from that read. A second
        // read of the same node does not see a change that came after the first.
        LoopState state = this.EvalNow(expression: expression, own: out LoopState own);

        if (!ReferenceEquals(objA: own, objB: state) && own.Objects.Contains(MadeObject.Unknown))
        {
            this.beforeTheStore[expression] = own;
        }

        if (this.region.Symbols.DeclaresConversions && this.ConvertedBy(expression: expression, value: state) is { } to)
        {
            state = to;
        }

        this.memo[expression] = state;

        return state;
    }

    // The state of an expression at this time, which Eval keeps for the node. Own is the state
    // before the store refreshed it.
    private LoopState EvalNow(ExpressionSyntax expression, out LoopState own)
    {
        own =
            this.OnlyObjectsThatWentIn(
                expression: expression,
                state: this.OnlyObjectsOfItsType(
                    expression: expression,
                    state: this.Now(this.Captured(expression: expression, state: this.EvalUncached(expression)))));

        if (this.Frame.KeptAt.Count > 0)
        {
            own = own.Join(this.KeptThrough(expression));
        }

        return this.Refreshed(expression: expression, state: own);
    }

    /// <summary>
    ///     The object in a member of an object of the store. Each read of the
    ///     member, through each name of the owner, is that one object. That is
    ///     true also when the walk did not read the constructor of the owner. A
    ///     value that is one of two owners has the object in the member of each.
    ///     Null for a member whose state has an object of the store, and for a
    ///     member that is not one of this compilation.
    /// </summary>
    /// <remarks>
    ///     A member of the platform, such as <c>Values</c> of a dictionary, keeps
    ///     the rule that a member of a pending value is pending. An evaluator that
    ///     reads code which does not run now makes no object.
    /// </remarks>
    private LoopState? InMember(LoopState owner, ISymbol member, ExpressionSyntax expression)
    {
        if (owner.Objects.IsEmpty
            || member.DeclaringSyntaxReferences.IsEmpty
            || this.region.TypeOf(expression) is not { } type
            || !this.CanBeAnObject(type))
        {
            return null;
        }

        LoopState inOwner =
            owner.Members != null && owner.Members.Has(member) ? owner.Members.StateOf(member) : LoopState.None;

        if (!inOwner.Objects.IsEmpty)
        {
            return null;
        }

        LoopState? all = null;

        foreach (MadeObject one in owner.Objects)
        {
            // An owner that the store does not know has a member that the store does not know.
            if (ReferenceEquals(objA: one, objB: MadeObject.Unknown))
            {
                all = (all ?? LoopState.None).OrAnUnknownObject;

                continue;
            }

            MadeObject? made = this.region.Objects.InMember(owner: one, member: member);

            if (made == null)
            {
                if (this.quiet)
                {
                    return null;
                }

                made =
                    this.region.Objects.MakeInMember(
                        owner: one,
                        member: member,
                        type: type,
                        state: inOwner.Join(this.TypeState(expression)));
            }

            if (this.region.Objects.StateOf(made) is not { } inThisOwner)
            {
                return null;
            }

            LoopState ofThisOwner = inThisOwner.WithObjects(ImmutableArray.Create(made));

            all = all == null ? ofThisOwner : all.Join(ofThisOwner);
        }

        return all?.FromIfUnknown(owner.Origin);
    }

    // A location can keep a lambda or a local function from a turn before, and a call then runs
    // that function. A local that the function reads from the code around it is then the local of
    // that turn. Thus, the value of such a local can be an object of the turns before also. A
    // function that this run of the code made reads the locals of this run.
    private LoopState Captured(SyntaxNode expression, LoopState state) =>
        this.function != null
        && this.call is not { RunsAFunctionOfThisRun: true }
        && expression is IdentifierNameSyntax
        && this.region.SymbolOf(expression) is { } local and (ILocalSymbol or IParameterSymbol)
        && !IsDeclaredIn(local: local, code: this.function)
            ? this.InAnOldLocation(state)
            : state;

    // After the walk of a loop, the object of its turn is one of the objects of the turns before.
    private LoopState Now(LoopState state)
    {
        ImmutableArray<MadeObject> now = this.region.Objects.Now(state.Objects);

        return now == state.Objects ? state : state.WithObjects(now);
    }

    // An element of a collection that holds only objects that the walk saw go in is one of those
    // objects. The local of such a collection can have an object that the store does not know,
    // from a copy that the local keeps. The element is not that object.
    private LoopState OnlyObjectsThatWentIn(ExpressionSyntax expression, LoopState state) =>
        expression is ElementAccessExpressionSyntax element
        && state.Objects.Contains(MadeObject.Unknown)
        && this.TheCompleteCollection(element.Expression) != null
            ? state.WithObjects(state.Objects.Remove(MadeObject.Unknown))
            : state;

    // An object that a value of the type of the expression cannot be is not the value. The element
    // of a dictionary is a pair, and not the object that the pair holds.
    private LoopState OnlyObjectsOfItsType(ExpressionSyntax expression, LoopState state)
    {
        if (state.Objects.IsEmpty)
        {
            return state;
        }

        ITypeSymbol? written = this.region.TypeOf(expression);
        ImmutableArray<MadeObject> kept = state.Objects.RemoveAll(made => !made.CanBe(written));

        return kept.Length == state.Objects.Length ? state : state.WithObjects(kept);
    }

    /// <summary>
    ///     The state of a value that is an object of the store, with the state
    ///     that the store has for the object. Each name of the object reads that
    ///     one state.
    /// </summary>
    /// <remarks>
    ///     A local, a parameter, a member, or an element gets the members of the
    ///     store when the store has each object that it can be. A collection holds
    ///     an object that the store does not know after it got a value with members
    ///     and no object. It holds one after a change through one of its elements
    ///     also, because the walk does not see each value that a collection gets.
    ///     Thus, an element of such a collection keeps what it has, as each value
    ///     that can be such an object does. The result of a call is one of the
    ///     values that it can be, and keeps what it has also.
    /// </remarks>
    private LoopState Refreshed(ExpressionSyntax expression, LoopState state)
    {
        if (state.Objects.IsEmpty || this.region.Objects.IsEmpty)
        {
            return state;
        }

        LoopState? stored = null;
        bool known = true;

        foreach (MadeObject made in state.Objects)
        {
            if (this.region.Objects.StateOf(made) is not { } one)
            {
                known = false;

                continue;
            }

            stored = stored == null ? one : stored.Join(one);
        }

        if (stored == null)
        {
            return state;
        }

        LoopState refreshed =
            state.RefreshedFrom(
                stored: stored.FromIfUnknown(state.AnyOrigin),
                all: known && this.IsANameOfItsObjects(Unwrapped(expression)));

        // The store has the parts of the object of this turn. A value that can be an older object
        // can have the older parts also. A local that is one collection is that object. Its state
        // can have an object that the store does not know, from a copy that the local keeps.
        LoopState parts =
            this.InAnObjectOf(
                turn: this.TheOneCollectionOf(expression) is { } only ? only.Turn : TurnOfTheNewOne(refreshed),
                state: refreshed);

        return ReferenceEquals(objA: parts, objB: refreshed) ? refreshed : parts.WithObjects(refreshed.Objects);
    }

    /// <summary>
    ///     The state of a value for a location that keeps a copy of it. The
    ///     location is a name, or a value that is one of two or more values.
    /// </summary>
    /// <remarks>
    ///     A read of the location adds the members that the store has at that
    ///     time. A copy of the members of the store in the location does not get
    ///     the next change to an object of the store. Thus, a value that reads only
    ///     the store gives no members. A value that can be an object that the store
    ///     does not know gives the members from before the store came in.
    /// </remarks>
    private LoopState Kept(ExpressionSyntax value)
    {
        ExpressionSyntax written = Unwrapped(value);
        LoopState state = this.Eval(written);

        if (!state.Objects.IsEmpty
            && state.Objects.All(made => this.region.Objects.StateOf(made) != null)
            && this.IsANameOfItsObjects(written))
        {
            return state.WithMembers(null);
        }

        return this.beforeTheStore.TryGetValue(key: written, value: out LoopState? own) ? own : state;
    }

    // True for an expression that is a name of the objects in its state. That is a local, a
    // parameter, a member, or an element. It is also the result of a method of the platform that
    // gives one value of its owner, as First does. The result of each other call is a value that
    // the callee made or got.
    private bool IsANameOfItsObjects(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax
        || (expression is InvocationExpressionSyntax call && this.OwnerOfTheResult(call) != null);

    /// <summary>
    ///     The state of a new object, with the object that the expression makes in
    ///     this frame. The store keeps the state.
    /// </summary>
    /// <remarks>
    ///     A struct is a copy at each assignment, thus a second name of it is not
    ///     the same object. It holds the objects of its parts in its members. An
    ///     evaluator that reads code which does not run now makes no object. The
    ///     body of a loop makes the object of this turn. Other code that can run
    ///     two or more times makes two or more objects. The store has none of
    ///     them, and each name then has its own state.
    /// </remarks>
    /// <param name="place">The expression that makes the object.</param>
    /// <param name="state">The state of the object.</param>
    /// <param name="exact">True when the object has the type of the expression, as a creation has.</param>
    private LoopState MadeHere(ExpressionSyntax place, LoopState state, bool exact)
    {
        TypeInfo info = this.region.ModelFor(place).GetTypeInfo(place);

        if ((info.Type ?? info.ConvertedType) is not { IsReferenceType: true } type)
        {
            return state.WithNoObjects;
        }

        // A new collection holds the objects that it got, and is not one of them. Members of a
        // value that no object of the store is are only here. Thus, the collection then holds an
        // object that the store does not know.
        LoopState apart = HoldingItsObjects(state);

        if (this.quiet)
        {
            return apart;
        }

        if (this.RunsOneTime(place))
        {
            MadeObject once =
                this.BuiltFor(place: place, state: apart)
                ?? this.region.Objects.Make(frame: this.Frame, type: type, exact: exact, state: apart);

            this.MarkIfComplete(made: once, place: place, exact: exact);

            return apart.WithObjects(ImmutableArray.Create(once));
        }

        // An expression of a callee, or of a function that the walk reads for a call, that runs
        // one time for that call. Its object is the object of the call.
        bool forTheCall = this.call != null && this.RunsOneTimeInThisCode(place);

        if (forTheCall && this.function != null && this.call is { RunsOneTime: true })
        {
            MadeObject one = this.region.Objects.Make(frame: this.Frame, type: type, exact: exact, state: apart);
            this.madeForTheCall.Add(one);

            return apart.WithObjects(ImmutableArray.Create(one));
        }

        if ((this.TurnOf(place) ?? (forTheCall ? this.call?.Turn : null)) is not { } turn)
        {
            return apart;
        }

        MadeObject made =
            this.region.Objects.MakeInATurn(
                frame: this.Frame,
                type: type,
                exact: exact,
                turn: turn,
                place: place,
                code: this.CodeOfThisCall,
                state: apart,
                around: this.LoopsAround(turn));

        if (forTheCall)
        {
            this.madeForTheCall.Add(made);
        }

        this.MarkIfComplete(made: made, place: place, exact: exact);

        return apart.WithObjects(ImmutableArray.Create(made));
    }

    // The bodies of the loops around a loop, the nearest first. The loops of this function come
    // first. Then comes the loop that runs the call of this code one time in each turn, with the
    // loops around that one.
    private ImmutableArray<SyntaxNode> LoopsAround(SyntaxNode turn)
    {
        if (this.call != null && turn == this.call.Turn)
        {
            return this.call.LoopsAroundTheTurn;
        }

        ImmutableArray<SyntaxNode>.Builder around = ImmutableArray.CreateBuilder<SyntaxNode>();

        for (SyntaxNode? owner = turn.Parent; owner != null && this.TurnOf(owner) is { } next; owner = next.Parent)
        {
            around.Add(next);
        }

        if (this.call is { Turn: { } ofTheCall })
        {
            around.Add(ofTheCall);
            around.AddRange(this.call.LoopsAroundTheTurn);
        }

        return around.ToImmutable();
    }

    /// <summary>
    ///     The body of the nearest loop around an expression. Null for an
    ///     expression in no loop of its function.
    /// </summary>
    /// <remarks>
    ///     An expression in a part of the loop that is not the body gets the body
    ///     also. No name of its object is new in each turn. Thus, each such name
    ///     can have an older object, and a write through it only adds.
    /// </remarks>
    private SyntaxNode? TurnOf(SyntaxNode place)
    {
        foreach (SyntaxNode ancestor in place.Ancestors())
        {
            if (LoopBodyOf(ancestor) is { } body)
            {
                return body;
            }

            switch (ancestor)
            {
                // A run of the lambda for one element is a turn, and the body of the lambda is its
                // body.
                case AnonymousFunctionExpressionSyntax lambda:
                    return this.Frame.RunsForEachElement.Contains(lambda) ? lambda.Body : null;
                case LocalFunctionStatementSyntax or QueryExpressionSyntax:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    ///     Records a lambda that a callee with no body to read runs one time for
    ///     each element. The walk then reads the lambda where it is.
    /// </summary>
    public void RunsForEachElement(SyntaxNode lambda) => this.Frame.RunsForEachElement.Add(lambda);

    /// <summary>
    ///     True when an expression runs one time at most for each run of the code
    ///     of this frame, and <see cref="LoopFrame.Repeats" /> is false for the
    ///     frame. An object that such an expression makes is one object.
    /// </summary>
    /// <remarks>
    ///     A loop runs its code again. A lambda, a local function, and a query can
    ///     run two or more times. The walk reads each of them in the frame of the
    ///     code that contains it.
    /// </remarks>
    private bool RunsOneTime(SyntaxNode place)
    {
        if (this.Frame.Repeats)
        {
            return false;
        }

        foreach (SyntaxNode ancestor in place.Ancestors())
        {
            if (ancestor == this.region.Root)
            {
                return true;
            }

            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda when this.RunsOneTimeInItsCall(lambda):
                    continue;
                case ForStatementSyntax
                    or CommonForEachStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
                    or AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or QueryExpressionSyntax:
                    return false;
                case MemberDeclarationSyntax or AccessorDeclarationSyntax:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The state of the result of a call, with an object for the call when the
    ///     store has no object for the result.
    /// </summary>
    /// <remarks>
    ///     The result is then a new object, or an object that the walk did not
    ///     see made. Only the names from this call have it, and each of them is a
    ///     name of that one object.
    /// </remarks>
    private LoopState ReturnedHere(ExpressionSyntax call, LoopState result)
    {
        if (!result.Objects.IsEmpty || this.region.TypeOf(call) is not { } written)
        {
            return result;
        }

        // A type parameter of this code is the type that the call of this code gave it.
        ITypeSymbol type = this.TypeArgumentFor(written) ?? written;

        if (!this.CanBeAnObject(type))
        {
            return result;
        }

        // A call that returns a location is a read of that location, which gives no new object.
        // A static member has the object that a read of it has. See LocationReturnedBy.
        if (call is InvocationExpressionSyntax invocation && this.ReturnedBy(invocation).Returned is { } returned)
        {
            if (Unwrapped(returned) is IdentifierNameSyntax or MemberAccessExpressionSyntax
                && this.region.SymbolOf(returned)
                    is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true }
                && this.Eval(returned) is { Objects.IsEmpty: false } read)
            {
                return result.Join(read);
            }

            if (this.LocationKeyOf(invocation) != null)
            {
                return result;
            }
        }

        // A sealed class of this compilation is the class of each of its objects. Thus, the result
        // has that class also when the walk did not read the callee.
        LoopState known =
            result.Members == null
            && type is INamedTypeSymbol
            {
                TypeKind: TypeKind.Class, IsSealed: true, DeclaringSyntaxReferences.IsEmpty: false
            } itsClass
                ? result.WithMembers(Construction.Of(itsClass))
                : result;

        return this.NotesAnOlderResult(call: call, made: this.MadeHere(place: call, state: known, exact: false));
    }

    // True for a type that does not say the class of its object: an interface, or a class that is
    // not sealed. Only the creation says the class of such a result, thus the walk reads the
    // callee for it. That read has a cost, and a callee with each other type of result gets none.
    private bool DoesNotSayItsClass(ITypeSymbol type) =>
        this.CanBeAnObject(type)
        && type is { TypeKind: TypeKind.Interface } or INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: false };

    // True for a type whose values the store keeps as objects. A string and a value of the library
    // do not change, and a delegate has its functions.
    private bool CanBeAnObject(ITypeSymbol type) =>
        type is { IsReferenceType: true, SpecialType: not SpecialType.System_String, TypeKind: not TypeKind.Delegate }
        && !this.region.Symbols.IsSodaFlow(type);

    /// <summary>
    ///     Changes the state of each object that a value can be. The new state
    ///     replaces the state of an object only when the change is sure to be to
    ///     that object. Each other change adds the new state.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The change is sure when three things are true. The value is one
    ///         object. The change always runs in its function. The code of this
    ///         frame, or of a callee of it, made the object. A caller can call a
    ///         callee in a branch. Thus, a change that a callee makes to an object
    ///         of its caller only adds.
    ///     </para>
    ///     <para>
    ///         For the object of a turn, the change must always run in each turn
    ///         of the loop that made the object. The objects of the turns before
    ///         are two or more objects, and a change to them only adds.
    ///     </para>
    /// </remarks>
    /// <param name="owner">The state of the value.</param>
    /// <param name="change">The new state of an object, from its state now.</param>
    /// <param name="replaces">True when the change always runs in its function.</param>
    /// <param name="turn">
    ///     The body of the loop whose each turn always runs the change. Null when
    ///     no loop does.
    /// </param>
    /// <param name="member">The member that the change gives a state, when the change is a write to one member.</param>
    private void Change(
        LoopState owner,
        Func<LoopState, LoopState> change,
        bool replaces,
        SyntaxNode? turn,
        ISymbol? member = null)
    {
        if (this.quiet)
        {
            return;
        }

        ImmutableArray<MadeObject> now = this.region.Objects.Now(owner.Objects);

        foreach (MadeObject made in now)
        {
            if (this.region.Objects.StateOf(made) is { } old)
            {
                LoopState changed = change(old);

                bool sure = now.Length == 1 && this.IsSureFor(made: made, replaces: replaces, turn: turn);

                // A write that each turn always runs, to an object that is older than the turn.
                // Until the end of the turn, the member has only the new state. After the loop,
                // it can have the state from before the loop also, because the loop can run no turn.
                // The loop is in this code, or in the code of a caller for a write of a callee. A
                // write of a callee has this rule for the object of the turn also.
                (SyntaxNode Turn, LoopFrame Frame)? inItsTurn =
                    sure || member == null || now.Length != 1 || made.IsOfTurnsBefore
                        ? null
                        : made.IsFrom(this.Frame)
                            ? turn != null && made.Turn != turn && IsTheBodyOfALoop(turn)
                                ? (turn, this.Frame)
                                : null
                            : replaces
                                ? this.TurnOfACaller
                                : null;

                if (inItsTurn is (var itsTurn, var itsFrame) && member != null)
                {
                    KeepsForAfterTheLoop(
                        frame: itsFrame,
                        turn: itsTurn,
                        made: made,
                        name: null,
                        member: member,
                        before: old.Members?.StateOf(member) ?? LoopState.None);
                }

                this.region.Objects.Set(found: made, state: sure || inItsTurn != null ? changed : old.Join(changed));
            }
        }
    }

    /// <summary>
    ///     The result of a call, when the body of the callee is in this
    ///     compilation. The call is an invocation, the creation of an object, or
    ///     the read of a property with a getter. It is also the constructor that a
    ///     constructor calls first. Null for a callee with no body to read, and for
    ///     a callee that is too deep.
    /// </summary>
    /// <summary>
    ///     The state of an expression as one object. A collection has the members
    ///     of its elements in its state, and the collection is not one of those
    ///     objects. Thus, a member that the code calls on the collection is not a
    ///     member of an element.
    /// </summary>
    /// <remarks>
    ///     The type of the expression tells the two apart, when an element cannot
    ///     have that type. A type such as <c>object</c> tells nothing. Then, the
    ///     state says if the value is a container.
    /// </remarks>
    public LoopState Self(ExpressionSyntax? written)
    {
        LoopState state = this.Eval(written);

        if (written == null || state.Members == null)
        {
            return state;
        }

        ITypeSymbol? type = this.TypeBeforeObject(written);

        if (!state.Members.CanBe(type) || (state.IsContainer && Construction.SaysNothing(type)))
        {
            return state.WithMembers(null);
        }

        // A value that is one of two objects is, through this type, only one that can have it.
        Construction narrowed = state.Members.Narrow(type);

        return ReferenceEquals(objA: narrowed, objB: state.Members) ? state : state.WithMembers(narrowed);
    }

    /// <summary>
    ///     The type of an expression, or the type that it has before a cast to
    ///     <c>object</c>. That cast keeps the value and hides what it is.
    /// </summary>
    private ITypeSymbol? TypeBeforeObject(ExpressionSyntax written)
    {
        ITypeSymbol? type = this.region.TypeOf(written);

        while (type?.SpecialType == SpecialType.System_Object)
        {
            ExpressionSyntax? inner =
                written switch
                {
                    ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                    CastExpressionSyntax cast => cast.Expression,
                    BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } conversion => conversion.Left,
                    _ => null
                };

            if (inner == null)
            {
                break;
            }

            written = inner;
            type = this.region.TypeOf(written);
        }

        return type;
    }

    public Followed? Follow(SyntaxNode call)
    {
        if (this.followed.TryGetValue(key: call, value: out Followed? known))
        {
            return known;
        }

        Followed? result =
            call switch
            {
                InvocationExpressionSyntax invocation => this.FollowInvocation(invocation),
                ObjectCreationExpressionSyntax creation =>
                    this.FollowCreation(creation: creation, arguments: creation.ArgumentList),
                ImplicitObjectCreationExpressionSyntax creation =>
                    this.FollowCreation(creation: creation, arguments: creation.ArgumentList),
                ConstructorInitializerSyntax chain => this.FollowChain(chain: chain, arguments: chain.ArgumentList),
                PrimaryConstructorBaseTypeSyntax chain =>
                    this.FollowChain(chain: chain, arguments: chain.ArgumentList),
                ExpressionSyntax access => this.FollowGetter(access),
                _ => null
            };

        this.followed[call] = result;

        return result;
    }

    private LoopState EvalUncached(ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax p => this.Eval(p.Expression),
            // A name with ref has the value that its location has at this time.
            RefExpressionSyntax r => this.Eval(r.Expression),
            // A cast that an operator of this compilation makes gives what the operator returns.
            CastExpressionSyntax c => this.FollowExpression(c) is { } converted
                ? converted.State.Join(this.TypeState(expression))
                : this.Eval(c.Expression),
            CheckedExpressionSyntax c => this.Eval(c.Expression),
            AwaitExpressionSyntax a => this.Eval(a.Expression),
            // The plus of two delegates makes a delegate that runs the functions of each.
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } b
                when this.region.TypeOf(b) is { TypeKind: TypeKind.Delegate } =>
                this.Eval(b.Left).Join(this.Eval(b.Right)),
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } s =>
                this.Eval(s.Operand),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } b => this.Eval(b.Left),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b => this.OneOf([b.Left, b.Right]),
            // An arm that a constant condition rules out gives no value.
            ConditionalExpressionSyntax c =>
                SinkWalker.ConstantOf(region: this.region, condition: c.Condition) is { } always
                    ? this.Eval(always ? c.WhenTrue : c.WhenFalse)
                    : this.OneOf([c.WhenTrue, c.WhenFalse]),
            // The value of an assignment is the value that its target gets. A coalescing assignment
            // gives the value that its target had, when that value is not null.
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a => this.Eval(a.Right),
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceAssignmentExpression } a =>
                this.Eval(a.Left).Join(this.Eval(a.Right)),
            // The value of a?.b is the value of .b, whose owner is a.
            ConditionalAccessExpressionSyntax c => this.Eval(c.WhenNotNull),
            SwitchExpressionSyntax s => this.OneOf([.. s.Arms.Select(static arm => arm.Expression)]),
            // A copy is a new object.
            WithExpressionSyntax w => this.MadeHere(
                place: w,
                state: this.WithInitializer(state: this.Eval(w.Expression), initializer: w.Initializer),
                exact: true),
            TupleExpressionSyntax t => this.EvalTuple(t),
            AnonymousObjectCreationExpressionSyntax a => this.EvalAnonymous(a),
            // A collection contains its elements, and is not one of them.
            CollectionExpressionSyntax c => this.MadeHere(
                place: c,
                state: this.JoinAll(
                        c.Elements.Select(static e =>
                            e switch
                            {
                                ExpressionElementSyntax x => x.Expression,
                                SpreadElementSyntax x => x.Expression,
                                _ => null
                            }))
                    .AsContainer,
                exact: true),
            ArrayCreationExpressionSyntax { Initializer: { } init } =>
                this.MadeHere(place: expression, state: this.JoinAll(init.Expressions).AsContainer, exact: true),
            // An array with a length and no element. It gets its elements after this.
            ArrayCreationExpressionSyntax => this.MadeHere(place: expression, state: LoopState.None, exact: true),
            ImplicitArrayCreationExpressionSyntax a => this.MadeHere(
                place: expression,
                state: this.JoinAll(a.Initializer.Expressions).AsContainer,
                exact: true),
            InitializerExpressionSyntax i => this.JoinAll(i.Expressions).AsContainer,
            // A query expression has the queries of its first source, and is one more. Its elements
            // are what its last select or group clause makes.
            QueryExpressionSyntax q => this.Eval(q.FromClause.Expression)
                .OnlyQueries
                .WithQuery(this.QueryAt(q))
                .Join(this.MadeBy(LastClauseOf(q.Body)).AsContainer),
            // An indexer of this compilation gives what its getter returns. An element of a
            // collection has the state of the collection.
            ElementAccessExpressionSyntax e when this.PositionOf(e) is { } position =>
                this.StateAtAPosition(position).Join(this.TypeState(expression)),
            ElementAccessExpressionSyntax e when this.AtAStorePosition(e) is { } known =>
                known.Join(this.TypeState(expression)),
            ElementAccessExpressionSyntax e => this.FollowExpression(e) is { } indexed
                ? indexed.State.Join(this.TypeState(expression))
                : ElementOf(
                        collection: this.TheArrayOfAViewOf(e) is { } viewed
                            ? this.EvalNow(expression: viewed, own: out _)
                            : this.Eval(e.Expression),
                        type: this.region.TypeOf(expression))
                    .Join(this.TypeState(expression)),
            // A lambda is a value. Its code runs when something calls it.
            AnonymousFunctionExpressionSyntax function =>
                LoopState.OfFunction(new FunctionValue(declaration: function, definer: this, receiver: null)),
            // The object that the code is in, with the members that the evaluator knows.
            ThisExpressionSyntax when this.thisInTheStore is { } built => this.ThisInTheStore(built),
            ThisExpressionSyntax when this.thisMembers.Count > 0 =>
                LoopState.None.WithMembers(
                    Construction.Of(this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default))),
            InvocationExpressionSyntax inv => this.WithTheResultOfATask(read: inv, state: this.EvalInvocation(inv)),
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => this.EvalCreation(expression),
            MemberAccessExpressionSyntax read when this.WrittenThroughANameOfItsObject(read) is { } written => written,
            MemberAccessExpressionSyntax read when this.WrittenThroughItsName(read) is { } written =>
                this.WithAnAccurateRecord(read: read, state: written),
            MemberAccessExpressionSyntax read when this.WrittenAtItsElement(read) is { } written =>
                this.WithAnAccurateRecord(read: read, state: written),
            IdentifierNameSyntax
                or MemberAccessExpressionSyntax
                or MemberBindingExpressionSyntax
                or GenericNameSyntax => this.WithTheSendsBefore(
                    name: expression,
                    state: this.WithItsInitializer(
                        expression: expression,
                        state: this.WithTheResultOfATask(read: expression, state: this.EvalName(expression)))),
            _ when expression.RawKind == SodaFlowSymbols.FieldKeywordKind => this.EvalFieldKeyword(expression),
            // An operator of this compilation gives what it returns.
            _ => this.FollowExpression(expression) is { } result
                ? result.State.Join(this.TypeState(expression))
                : this.TypeState(expression)
        };

    /// <summary>
    ///     A tuple has the state of all its elements together, for code that uses
    ///     it as one value. It also has the state of each element as a member.
    ///     Thus, a read of a plain element is not a read of a pending one.
    /// </summary>
    private LoopState EvalTuple(TupleExpressionSyntax tuple)
    {
        LoopState all = this.JoinAll(tuple.Arguments.Select(static argument => argument.Expression));
        TypeInfo info = this.region.ModelFor(tuple).GetTypeInfo(tuple);

        if ((info.Type ?? info.ConvertedType) is not INamedTypeSymbol { IsTupleType: true } type
            || type.TupleElements.Length != tuple.Arguments.Count)
        {
            return all;
        }

        Construction elements = Construction.Empty;

        for (int i = 0; i < tuple.Arguments.Count; i++)
        {
            elements = elements.With(member: type.TupleElements[i], state: this.Eval(tuple.Arguments[i].Expression));
        }

        return all.WithMembers(elements);
    }

    /// <summary>
    ///     An anonymous object has the state of each value that it gets as the
    ///     state of that property.
    /// </summary>
    private LoopState EvalAnonymous(AnonymousObjectCreationExpressionSyntax creation)
    {
        SemanticModel model = this.region.ModelFor(creation);
        Construction properties = Construction.Empty;

        foreach (AnonymousObjectMemberDeclaratorSyntax declarator in creation.Initializers)
        {
            if (model.GetDeclaredSymbol(declarator) is { } property)
            {
                properties = properties.With(member: property, state: this.Eval(declarator.Expression));
            }
        }

        return LoopState.None.WithMembers(properties);
    }

    /// <summary>
    ///     The state of one element of a collection. An element is one of the
    ///     objects that the collection contains. An element of a list of lists is
    ///     a list, which contains them also. The type of the element tells the
    ///     two apart. It is a sequence, and the objects that have the members
    ///     cannot be a value of it. An element of a tuple is one of its elements,
    ///     and the evaluator does not know which.
    /// </summary>
    /// <param name="collection">The state of the collection.</param>
    /// <param name="type">The type of the element.</param>
    public static LoopState ElementOf(LoopState collection, ITypeSymbol? type)
    {
        // The indexer of ITuple gives one element of a tuple, as an object. A list of tuples
        // gives a tuple, and its state has the mark of a container.
        if (collection is { IsContainer: false, Members: { IsTuple: true } tuple }
            && type is not INamedTypeSymbol { IsTupleType: true })
        {
            return tuple.TupleElements.Aggregate(
                seed: LoopState.None,
                func: static (all, element) => all.Join(element));
        }

        return SodaFlowSymbols.IsSequence(type) && collection.Members is { } members && !members.CanBe(type)
            ? collection
            : collection.AsOneOfItsElements;
    }

    /// <summary>
    ///     The collection that an expression names one value of, or null. The
    ///     expression is an index, a local that has an element, or a property of
    ///     the platform such as <c>Values</c>. It can also be a call of a method of
    ///     the platform that gives one value of its owner, such as <c>Find</c>.
    /// </summary>
    private ExpressionSyntax? CollectionOf(ExpressionSyntax element) =>
        Unwrapped(element) switch
        {
            ElementAccessExpressionSyntax indexed => indexed.Expression,
            IdentifierNameSyntax name
                when this.region.SymbolOf(name) is { } local and (ILocalSymbol or IParameterSymbol)
                     && this.Frame.ElementOf.TryGetValue(key: local, value: out ExpressionSyntax? owner) => owner,
            MemberAccessExpressionSyntax access
                when this.region.SymbolOf(access)
                         is IPropertySymbol { IsStatic: false, DeclaringSyntaxReferences.IsEmpty: true } property
                     && !this.region.Symbols.IsSodaFlow(property) => access.Expression,
            InvocationExpressionSyntax call => this.OwnerOfTheResult(call),
            _ => null
        };

    // The definition of some methods of the platform has a type parameter as its result. Such a
    // method gives one of the values that its owner has, as Find, Last, and ElementAt do. ToList
    // makes a collection.
    private ExpressionSyntax? OwnerOfTheResult(InvocationExpressionSyntax call) =>
        this.region.SymbolOf(call) is IMethodSymbol method
        && (method.ReducedFrom ?? method).OriginalDefinition.ReturnType is ITypeParameterSymbol
        && this.Classify(call: call, method: method) == CalleeKind.Mover
            ? ReceiverOf(symbols: this.region.Symbols, invocation: call, method: method)
            : null;

    // The expression in parentheses, in a cast, after ref, or before the operator that says not
    // null.
    public static ExpressionSyntax Unwrapped(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax around:
                    expression = around.Expression;
                    continue;
                case RefExpressionSyntax location:
                    expression = location.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } sure:
                    expression = sure.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    // The clause that makes the elements of the result of a query body. A body with a
    // continuation gives its elements to the body after it.
    private static SelectOrGroupClauseSyntax LastClauseOf(QueryBodySyntax body)
    {
        while (body.Continuation != null)
        {
            body = body.Continuation.Body;
        }

        return body.SelectOrGroup;
    }

    /// <summary>
    ///     The state of one element that a select or a group clause makes. A group
    ///     has the state of its elements and of its key together, as a pair from a
    ///     dictionary has.
    /// </summary>
    private LoopState MadeBy(SelectOrGroupClauseSyntax clause) =>
        clause switch
        {
            SelectClauseSyntax select => this.Eval(select.Expression),
            GroupClauseSyntax group =>
                this.Eval(group.GroupExpression).AsContainer.Join(this.Eval(group.ByExpression).AsContainer),
            _ => LoopState.None
        };

    /// <summary>
    ///     The state of a range variable of a query expression, from the clause
    ///     that declares it. The variable of a from or a join clause is an element
    ///     of its source. The variable of a let clause has the state of its
    ///     expression. The variable after <c>into</c> is the group of a join, or
    ///     what the clause before it makes.
    /// </summary>
    private LoopState EvalRangeVariable(ExpressionSyntax expression, ISymbol variable) =>
        variable.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
        {
            FromClauseSyntax from =>
                ElementOf(collection: this.Eval(from.Expression), type: this.region.TypeOf(expression)),
            JoinClauseSyntax join =>
                ElementOf(collection: this.Eval(join.InExpression), type: this.region.TypeOf(expression)),
            JoinIntoClauseSyntax { Parent: JoinClauseSyntax join } => this.Eval(join.InExpression),
            LetClauseSyntax let => this.Eval(let.Expression),
            QueryContinuationSyntax { Parent: QueryBodySyntax before } => this.MadeBy(before.SelectOrGroup),
            _ => LoopState.None
        };

    private LoopState EvalName(ExpressionSyntax expression)
    {
        ISymbol? symbol = this.region.SymbolOf(expression);

        switch (symbol)
        {
            case IRangeVariableSymbol variable:
                return this.EvalRangeVariable(expression: expression, variable: variable);
            case ILocalSymbol or IParameterSymbol:
            {
                if (SymbolEqualityComparer.Default.Equals(x: symbol, y: this.region.Placeholder))
                {
                    return this.region.PlaceholderState;
                }

                // A name with ref has the value that its location has now. A second name of a
                // value with no object has the state of the first name. Each record holds from the
                // code that made it up to this use.
                if (!ReferenceEquals(objA: expression, objB: this.readForAUse)
                    && (this.TheLocationOf(expression) is { } itsLocation
                        ? this.TheFirstNameOf(written: itsLocation, at: expression) ?? itsLocation
                        : this.TheFirstNameOf(expression)) is { } location)
                {
                    return this.EvalAgainForAUse(location);
                }

                // A parameter with ref, out, or in has the value that its location has now in the
                // code of the caller.
                if (this.ReadInTheCaller(expression) is { } inTheCaller)
                {
                    return inTheCaller;
                }

                LoopState named =
                    !this.assigned.Contains(symbol)
                    && this.bound.TryGetValue(key: symbol, value: out LoopState? boundState)
                        ? boundState
                        : LoopState.None;

                if (this.locals.TryGetValue(key: symbol, value: out LoopState? localState))
                {
                    named = named.Join(localState);
                }
                // Only a function can read a local of a loop body before the declaration of this turn.
                // The function is then one of a turn before, and the local is the local of that turn.
                else if (this.Frame.OfTheTurnBefore.TryGetValue(key: symbol, value: out LoopState? before))
                {
                    named = named.Join(before);
                }

                named = this.WithItsField(local: symbol, use: expression, state: named);

                // A parameter with ref, out, or in whose location the walk cannot read.
                if (symbol is IParameterSymbol { RefKind: not RefKind.None } aliased)
                {
                    named = this.WithThePutsOfThisCode(parameter: aliased, state: named);
                }

                // A method of an object can read a parameter of the primary constructor of the
                // object.
                return named.IsNone
                       && symbol is IParameterSymbol
                       && this.thisMembers.TryGetValue(key: symbol, value: out LoopState? captured)
                    ? captured
                    : named;
            }
            case IMethodSymbol group:
                return this.EvalMethodGroup(expression: expression, group: group);
            // An event that keeps its handlers in a field has them as a field has its value.
            case IEventSymbol when SodaFlowSymbols.IsFieldLikeEvent(symbol):
            case IPropertySymbol or IFieldSymbol:
            {
                if (symbol is IPropertySymbol property)
                {
                    // A property with a getter has the state of what the getter returns. Only a
                    // property of this compilation has a getter that the evaluator can read. The
                    // value of a Lazy is what its function returns.
                    if ((property.DeclaringSyntaxReferences.Length > 0 || SodaFlowSymbols.IsLazyValue(property))
                        && this.Follow(expression) is { } getter)
                    {
                        LoopState got = getter.State.Join(this.TypeState(expression));

                        // The getter of an iterator runs when something enumerates the result.
                        return IsIteratorProperty(property) ? got.WithQuery(this.QueryAt(expression)) : got;
                    }

                    // A property of the library that reads the current value gives the value that
                    // its owner holds. A bindable of a hold of the block reads that hold, and a read
                    // in the transaction then gets its initial value.
                    if (this.region.Symbols.ReadsItsOwner(property))
                    {
                        ExpressionSyntax? owner =
                            expression switch
                            {
                                MemberAccessExpressionSyntax read => read.Expression,
                                MemberBindingExpressionSyntax read => ConditionalReceiver(read),
                                _ => null
                            };

                        return (this.InitialOfAHoldOfTheBlock(read: expression, receiver: owner)
                                ?? this.Eval(owner).Content)
                            .Join(this.TypeState(expression));
                    }
                }

                // A field that the code set while the loop is open, on an object that the walk does
                // not know. A put through a local that got the array of the field is a put into the
                // field also.
                LoopState field =
                    (this.region.HasFields
                        ? this.region.FieldState(field: symbol, canHave: this.KeysOfTheResult(expression))
                        : LoopState.None)
                    .Join(this.region.PutsThroughLocalsOf(symbol));

                // A member of this object, in a constructor the evaluator follows, has the state the
                // constructor gave it.
                if (expression is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
                {
                    return this.OfThisObject(
                        member: symbol,
                        field: field,
                        read: expression,
                        throughThisOrAField: true);
                }

                // The field has the state from each owner that the walk does not know. The owner
                // of this read can be a different one, with an object that the store does not know.
                LoopState fromOwner = field.Objects.IsEmpty ? field : field.OrAnUnknownObject;

                ExpressionSyntax? ownerExpression =
                    expression switch
                    {
                        MemberAccessExpressionSyntax access => access.Expression,
                        MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
                        _ => null
                    };

                // A name that got this reads the members of the object that the code is in.
                if (ownerExpression != null && this.IsANameOfThis(ownerExpression))
                {
                    return this.OfThisObject(
                        member: symbol,
                        field: field,
                        read: expression,
                        throughThisOrAField: this.RootOf(ownerExpression) is { } root
                                             && this.region.SymbolOf(root) is IFieldSymbol or IPropertySymbol);
                }

                if (ownerExpression != null)
                {
                    LoopState owner = this.Eval(ownerExpression);

                    if (this.InMember(owner: owner, member: symbol, expression: expression) is { } inMember)
                    {
                        return inMember;
                    }

                    if (InnerExceptionsOf(owner: owner, member: symbol) is { } inner)
                    {
                        return inner.Join(this.TypeState(expression));
                    }

                    // An object whose construction was followed gives each member its own state,
                    // wherever the object came from: a local, a hold, or a lambda parameter. The
                    // code can read the member through an interface of the object. A tuple with
                    // no state for the element is a tuple that the evaluator did not see made.
                    // A pair, a task, a nullable, or an enumerator of the platform has the members
                    // of the object that it contains. Its own property is not one of them.
                    // A pair that the evaluator saw made has a state for each of its parts.
                    if (owner.Members != null
                        && SodaFlowSymbols.HoldsParts(symbol.ContainingType)
                        && owner.Members.Has(symbol))
                    {
                        return owner.Members.StateOf(symbol)
                            .FromIfUnknown(owner.Origin)
                            .Join(this.TypeState(expression));
                    }

                    if (owner.Members != null
                        && owner.Members.CanOwn(symbol)
                        && (!symbol.ContainingType.IsTupleType || owner.Members.Has(symbol)))
                    {
                        LoopState through =
                            owner.Members.StateThrough(
                                member: symbol,
                                implementations: this.region.Symbols.Implementations);

                        // A write through the other name of the value did not come to this name.
                        // The region has each such write for the field.
                        if (this.WasOneOfTwoNames(ownerExpression)
                            || this.CanBeWrittenThroughAField(owner: owner, written: ownerExpression)
                            || this.ReadsAnOlderObject(owner: owner, member: symbol, field: field))
                        {
                            through = through.Join(field);
                        }

                        through = through.Join(
                            this.WrittenAfterItsOwner(
                                owner: ownerExpression,
                                has: owner,
                                member: symbol,
                                field: field));

                        // The members are of each object that the owner can be. An owner that the
                        // store does not know has a member that the store does not know.
                        return (through.Objects.IsEmpty || !owner.Objects.Contains(MadeObject.Unknown)
                                ? through
                                : through.OrAnUnknownObject)
                            .FromIfUnknown(owner.Origin)
                            .Join(this.TypeState(expression));
                    }

                    // Otherwise, a member of a pending value is pending. Such a member is the cell
                    // behind a bindable, one element of a tuple from a loop block, or the values of
                    // a dictionary. A member that is not a sequence is one of the values that the
                    // owner contains, as the value of a pair is.
                    ITypeSymbol memberType =
                        symbol switch
                        {
                            IPropertySymbol p => p.Type,
                            IEventSymbol e => e.Type,
                            _ => ((IFieldSymbol)symbol).Type
                        };

                    // SyncRoot of a collection is an object to lock, and not a value in it.
                    if (symbol.ContainingType.IsTupleType
                        || (symbol.Name != "SyncRoot"
                            && this.region.Symbols.CanHoldState(
                                type: memberType,
                                compilation: this.region.Assembly)))
                    {
                        // A dictionary holds its values. Its keys are objects that the store does not
                        // know as objects of the dictionary, thus a key keeps what it has.
                        LoopState ofOwner = symbol.Name == "Keys" ? owner.WithNoObjects.HoldingAnUnknownObject : owner;

                        fromOwner =
                            fromOwner.Join(SodaFlowSymbols.IsSequence(memberType) ? ofOwner : ofOwner.AsElement);
                    }
                    else if (SodaFlowSymbols.CanHoldQuery(memberType))
                    {
                        fromOwner = fromOwner.Join(owner.OnlyQueries);
                    }
                }

                return fromOwner.Join(this.TypeState(expression));
            }
            default:
                return this.TypeState(expression);
        }
    }

    // The field keyword of C# 14 in an accessor of a property. It has the state of the property of
    // this object, as a name of an automatic property in a constructor has.
    private LoopState EvalFieldKeyword(ExpressionSyntax expression)
    {
        if (SodaFlowSymbols.PropertyOfTheFieldKeyword(expression: expression, symbol: this.region.SymbolOf(expression))
            is not { } property)
        {
            return this.TypeState(expression);
        }

        return (this.thisMembers.TryGetValue(key: property.OriginalDefinition, value: out LoopState? own)
                ? own
                : LoopState.None)
            .Join(this.region.HasFields ? this.region.FieldState(property) : LoopState.None)
            .Join(this.TypeState(expression));
    }

    /// <summary>
    ///     The name of a method, used as a value. A method or a local function of
    ///     this compilation is a function that a call of the delegate runs.
    /// </summary>
    private LoopState EvalMethodGroup(ExpressionSyntax expression, IMethodSymbol group)
    {
        // A method group such as x.Sample is a read that runs after this. It is pending, as a lazy
        // sample of x is.
        if (expression is MemberAccessExpressionSyntax read
            && SodaFlowSymbols.ReadMembers.Contains(read.Name.Identifier.ValueText)
            && this.region.Symbols.IsSodaFlow(group))
        {
            return this.Eval(read.Expression).WithLinked(false);
        }

        SyntaxNode? declaration =
            (group.ReducedFrom ?? group).OriginalDefinition.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .FirstOrDefault(static syntax => syntax is MethodDeclarationSyntax or LocalFunctionStatementSyntax);

        if (declaration == null)
        {
            return this.TypeState(expression);
        }

        return LoopState.OfFunction(
            new FunctionValue(
                declaration: declaration,
                definer: declaration is LocalFunctionStatementSyntax ? this : null,
                receiver: expression is MemberAccessExpressionSyntax { Expression: not ThisExpressionSyntax } owned
                    ? this.Eval(owned.Expression).Members
                    : null));
    }

    private LoopState EvalInvocation(InvocationExpressionSyntax invocation)
    {
        if (this.region.SymbolOf(invocation) is not IMethodSymbol method)
        {
            return this.TypeState(invocation);
        }

        // First and Last of a collection that keeps its positions give one element.
        if (invocation is
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "First" or "Last" } ends
            })
        {
            if (this.ElementsOf(ends.Expression) is { Count: > 0 } all)
            {
                return this.Eval(method.Name == "First" ? all[0] : all[all.Count - 1])
                    .Join(this.TypeState(invocation));
            }

            // The same for a First or a Last of the platform, from the positions of the store.
            if (method.ReducedFrom is { ContainingType: { } queries }
                && SodaFlowSymbols.IsLinqEnumerable(queries)
                && this.StorePositionsOf(ends.Expression) is (_, { Length: > 0 } values))
            {
                return (method.Name == "First" ? values[0] : values[values.Length - 1])
                    .Join(this.TypeState(invocation));
            }
        }

        if (this.AtThePositionOfACall(invocation: invocation, method: method) is { } element)
        {
            return element.Join(this.TypeState(invocation));
        }

        // Code that is not the library. A body in this compilation gives the state. Code that
        // cannot know the library only moves a value, and its result has the state of its inputs.
        // Any other callee is unknown, and a delegate with no known function is one. The result
        // gets no state, and the walk reports a pending argument with SF1003. A warning is for
        // what the analyzer can see.
        if (!this.region.Symbols.IsSodaFlow(method))
        {
            if (this.StateOfADelegateCall(invocation: invocation, method: method) is { } functions)
            {
                return functions;
            }

            CalleeKind kind = this.Classify(call: invocation, method: method);

            LoopState result =
                kind == CalleeKind.Mover
                    ? this.WithParts(
                        state: this.MovedResult(invocation: invocation, method: method),
                        // Only the factory of the type itself puts each argument in its property.
                        made: SodaFlowSymbols.HoldsParts(method.ContainingType) ? method.ReturnType : null,
                        callee: method,
                        arguments: invocation.ArgumentList.Arguments)
                    : LoopState.None;

            // The result is a new object, or one that the store does not know, and holds what
            // the callee got. A method whose result is a type parameter, such as First, gives one
            // of the objects that it got.
            if ((method.ReducedFrom ?? method).OriginalDefinition.ReturnType is not ITypeParameterSymbol)
            {
                result = result.WithObjects(ImmutableArray<MadeObject>.Empty);
            }

            if (kind == CalleeKind.Unknown)
            {
                result = result.Join(this.KeptByTheOwnerOf(invocation: invocation, method: method));
            }

            result =
                result
                    .Join(this.Follow(invocation)?.State ?? LoopState.None)
                    .Join(this.region.Symbols.StateOfType(method.ReturnType));

            result = this.ReturnedHere(call: invocation, result: result);

            // A query and the result of an iterator keep their code for the time when
            // something enumerates them.
            return this.Waits(call: invocation, method: method)
                ? result.WithQuery(this.QueryAt(invocation))
                : result;
        }

        ExpressionSyntax? receiver =
            ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        LoopState receiverState = this.Eval(receiver);
        ITypeSymbol? receiverType = receiver == null ? null : this.region.TypeOf(receiver);
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        List<LoopState> argumentStates = [];
        List<ITypeSymbol?> argumentTypes = [];
        bool sortsByPendingOrder = false;

        // In a static call of an extension method, the receiver is one of the arguments. It is not
        // also an argument of the operator.
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression == receiver)
            {
                continue;
            }

            IParameterSymbol? parameter = ParameterFor(method: method, argument: arguments[i], position: i);

            // A comparer is a function of the operator, and not a value that the result holds. The
            // members of a comparer object are not members of the result.
            LoopState argumentState =
                SodaFlowSymbols.IsComparer(parameter?.Type)
                    ? LoopState.None
                    : this.EvalArgument(
                        argument: arguments[i].Expression,
                        runsNow: OperatorTable.RunsFunction(method: method, parameter: parameter));

            argumentStates.Add(argumentState);
            argumentTypes.Add(this.region.TypeOf(arguments[i].Expression));

            sortsByPendingOrder |=
                argumentState.ContentPending
                && OperatorTable.IsOrderCell(symbols: this.region.Symbols, parameter: parameter);
        }

        LoopState inputs = argumentStates.Aggregate(seed: receiverState, func: static (a, b) => a.Join(b));
        LoopState firstArgument = argumentStates.Count > 0 ? argumentStates[0] : LoopState.None;
        LoopState source = receiver != null ? receiverState : firstArgument;

        OperatorRule rule =
            OperatorTable.Rule(symbols: this.region.Symbols, method: method, receiverType: receiverType);

        LoopState state =
            rule switch
            {
                // A function that is not written at the call is a lambda in a local, a local
                // function, or a method. The evaluator follows it.
                // The result is an object when the function returns one. A function that the walk
                // did not read gives no object, and the result then gets the object of the call.
                OperatorRule.RunsNow => this.ReturnedHere(
                    call: invocation,
                    result: firstArgument.Join(this.Follow(invocation)?.State ?? LoopState.None)),
                // A read gives the value that the receiver holds, with the state of that value. A
                // read in the block of a hold that the block made gives the initial value.
                OperatorRule.Read => this.InitialOfAHoldOfTheBlock(read: invocation, receiver: receiver)
                                     ?? receiverState.Content,
                // The lazy sample gives the value that the receiver holds.
                OperatorRule.SampleLazy => new LoopState(
                    pending: receiverState.Pending,
                    linked: false,
                    contentPending: false,
                    contentLinked: false).WithContent(receiverState.Content),
                OperatorRule.MapLazy => this.HoldsContent(method)
                    ? receiverState.WithLinked(false)
                        .WithContent(this.MappedContent(invocation: invocation, parameterState: receiverState.Content))
                    : receiverState.WithLinked(false),
                OperatorRule.MapStream => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.MappedContent(invocation: invocation, parameterState: receiverState.Content)),
                // The result holds what the function returns, and not what the receiver held.
                OperatorRule.MapValue => new LoopState(
                    pending: receiverState.Pending,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.MappedContent(invocation: invocation, parameterState: receiverState.Content)),
                OperatorRule.LiftLazy => this.HoldsContent(method)
                    ? inputs.WithLinked(false)
                        .WithContent(
                            this.LiftedContent(
                                invocation: invocation,
                                receiver: receiver,
                                receiverState: receiverState,
                                argumentStates: argumentStates))
                    : inputs.WithLinked(false),
                OperatorRule.Combine => new LoopState(
                    pending: inputs.Pending,
                    linked: inputs.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.AppliedContent(
                        invocation: invocation,
                        method: method,
                        receiver: receiver,
                        receiverState: receiverState,
                        functionCell: firstArgument)),
                OperatorRule.LiftValue => new LoopState(
                    pending: inputs.Pending,
                    linked: inputs.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.HoldsContent(method)
                        ? this.LiftedContent(
                            invocation: invocation,
                            receiver: receiver,
                            receiverState: receiverState,
                            argumentStates: argumentStates)
                        : LoopState.None),
                OperatorRule.SameValues => new LoopState(
                    pending: inputs.Pending,
                    linked: inputs.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(inputs.Content),
                // A send through the result is a send into the sink of the receiver, and reaches each
                // hold of that sink.
                OperatorRule.Receiver => receiverState,
                // A switch holds what the selected cell or stream holds.
                OperatorRule.SwitchValue => new LoopState(
                    pending: receiverState.Pending || receiverState.ContentPending,
                    linked: receiverState.Linked || receiverState.ContentLinked,
                    contentPending: false,
                    contentLinked: false).WithContent(receiverState.Content.Content),
                OperatorRule.SwitchStream => new LoopState(
                    pending: false,
                    linked: receiverState.ContentLinked,
                    contentPending: false,
                    contentLinked: false).WithContent(receiverState.Content.Content),
                // A read before the close gives the initial value. The value of a firing comes
                // after the close.
                OperatorRule.Hold => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(receiverState.Content.AfterTheClose.Join(firstArgument)),
                OperatorRule.HoldLazy => new LoopState(
                    pending: firstArgument.Pending,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.HeldContent(
                        invocation: invocation,
                        method: method,
                        receiverState: receiverState,
                        initial: firstArgument.Content)),
                OperatorRule.ConstantLazy => firstArgument.WithLinked(false),
                // A value given to a constant or a sink is content. The links of a value with members
                // of its own are in it, and not links of the cell. A function is a value also.
                OperatorRule.Content => firstArgument.Join(this.KeptFunction(arguments: arguments, receiver: receiver))
                    .AsContent,
                OperatorRule.Fold => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    OperatorName(method) == "Collect"
                        ? this.CollectedContent(
                            invocation: invocation,
                            method: method,
                            receiverState: receiverState,
                            initial: firstArgument)
                        : this.HeldContent(
                            invocation: invocation,
                            method: method,
                            receiverState: receiverState,
                            initial: firstArgument)),
                // The updates of a sink fire what the sends give it, and not its initial value.
                OperatorRule.Updates => OperatorName(method) == "Updates" && this.SentToTheSinks(source) is { } sent
                    ? source.WithPending(false).WithContent(sent)
                    : source.WithPending(false),
                OperatorRule.MapTo => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(firstArgument),
                // A snapshot samples its cells at each firing. Only the stream has a link.
                OperatorRule.StreamFromReceiver => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(receiverState.Content),
                OperatorRule.Snapshot => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.SnapshotContent(
                        invocation: invocation,
                        method: method,
                        receiverState: receiverState,
                        argumentStates: argumentStates)),
                // A function that the operator keeps gives no input. Thus, the links are of the
                // stream and of each cell.
                OperatorRule.SnapshotLatest => new LoopState(
                    pending: false,
                    linked: inputs.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.SnapshotContent(
                        invocation: invocation,
                        method: method,
                        receiverState: receiverState,
                        argumentStates: argumentStates)),
                OperatorRule.MergeStreams => new LoopState(
                    pending: false,
                    linked: LinkedStreamInput(
                        symbols: this.region.Symbols,
                        receiverState: receiverState,
                        receiverType: receiverType,
                        argumentStates: argumentStates,
                        argumentTypes: argumentTypes),
                    contentPending: false,
                    contentLinked: false).WithContent(
                    inputs.Content.Join(
                        this.MergedContent(
                            invocation: invocation,
                            method: method,
                            receiverType: receiverType,
                            receiverState: receiverState,
                            argumentStates: argumentStates))),
                OperatorRule.DeferLazy => new LoopState(
                    pending: firstArgument.Pending,
                    linked: false,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.HoldsContent(method)
                        ? this.MappedContent(invocation: invocation, parameterState: LoopState.None)
                        : LoopState.None),
                // A defer, a split, and a debounce fire in a new transaction. Thus, they have no link,
                // and fire what their stream fires, or each element of it.
                OperatorRule.Later => LoopState.None.WithContent(source.Content.AsOneOfItsElements),
                // A timer, a listener, and a send: no state.
                OperatorRule.Break => LoopState.None,
                OperatorRule.Default or OperatorRule.FoldCollection => DefaultLibraryResult(
                    symbols: this.region.Symbols,
                    method: method,
                    inputs: inputs),
                OperatorRule.BindableAction => DefaultLibraryResult(
                        symbols: this.region.Symbols,
                        method: method,
                        inputs: inputs)
                    .WithMembers(this.ActionParts(method: method, arguments: arguments)),
                // A transform gives the edit the state that an item of an add gives it.
                OperatorRule.CollectionUpdate => this.region.Symbols.IsStreamType(method.ReturnType)
                    ? DefaultLibraryResult(symbols: this.region.Symbols, method: method, inputs: inputs)
                        .WithContent(
                            new LoopState(
                                    pending: inputs.ContentPending,
                                    linked: inputs.ContentLinked,
                                    contentPending: false,
                                    contentLinked: false)
                                .Join(
                                    this.TransformedContent(
                                        invocation: invocation,
                                        method: method,
                                        argumentStates: argumentStates)))
                    : DefaultLibraryResult(symbols: this.region.Symbols, method: method, inputs: inputs)
                        .Join(
                            this.TransformedContent(
                                invocation: invocation,
                                method: method,
                                argumentStates: argumentStates))
                        .Join(this.TransformsOf(invocation: invocation, method: method)),
                OperatorRule.CollectionCreate => this.MadeCollection(
                    inputs: inputs,
                    argumentStates: argumentStates,
                    argumentTypes: argumentTypes),
                _ => throw new ArgumentOutOfRangeException(
                    paramName: nameof(invocation),
                    message: "An operator rule has no case.")
            };

        // A collection enumerates the sequence of its initial items at the first read of it. A
        // read in that sequence makes the collection pending.
        if (!state.Pending
            && OperatorTable.EnumeratesAfterTheClose(method)
            && this.SequenceReadsPending(invocation: invocation, method: method))
        {
            state = state.WithPending(true);
        }

        // The function of a map, a lift, or a view computes the value of the result at the first
        // read of it. A function that reads a pending value makes that value pending. A view that
        // sorts by the order in a cell runs the function of that order.
        IReadOnlyList<Finding> reads = [];

        if (!state.Pending
            && (sortsByPendingOrder
                || (OperatorTable.ComputesValueWithFunction(
                        symbols: this.region.Symbols,
                        method: method,
                        receiverType: receiverType)
                    && this.CanUseState(invocation: invocation, given: inputs.Content)
                    && (reads = this.Quiet()
                            .FunctionReadsPending(invocation: invocation, method: method, given: inputs.Content))
                        .Count > 0)
                || ((this.region.Symbols.DeclaredCallbacks & PlatformCallbacks.Compare) != PlatformCallbacks.None
                    && OperatorTable.SortsWithTheDefaultComparer(method)
                    && this.Quiet()
                        .SortValuesReadPending(invocation: invocation, method: method, given: inputs.Content))))
        {
            state = state.WithPending(true).FromIfUnknown(this.OriginOfARead(reads));
        }

        state = this.WithTheComputedValue(
            state: state,
            rule: rule,
            invocation: invocation,
            method: method,
            receiverType: receiverType,
            receiverState: receiverState,
            firstArgument: firstArgument,
            inputs: inputs);

        if (this.region.ReadsBack)
        {
            state = this.WithReadBack(
                rule: rule,
                state: state,
                invocation: invocation,
                method: method,
                receiverState: receiverState,
                receiverType: receiverType,
                firstArgument: firstArgument,
                inputs: inputs);
        }

        // The result of an operator came into this code as its inputs did.
        state = state.FromIfUnknown(inputs.Origin);

        return rule == OperatorRule.Content && OperatorName(method) is "CreateSink" or "CreateStreamSink"
            ? this.ASink(creation: invocation, method: method, state: state)
            : method.Name is "CreateTwoWay" or "Construct" or "Map"
                ? this.WithTheLibraryParts(call: invocation, method: method, state: state)
                : state;
    }

    /// <summary>
    ///     An object made in the region. Its constructor is followed when it is
    ///     in this compilation, and the object then knows the state of each of its
    ///     members.
    /// </summary>
    private LoopState EvalCreation(ExpressionSyntax creation)
    {
        InitializerExpressionSyntax? initializer =
            creation switch
            {
                ObjectCreationExpressionSyntax o => o.Initializer,
                ImplicitObjectCreationExpressionSyntax i => i.Initializer,
                _ => null
            };

        // The object has no node of its own. Its members hold the state, and a hold or a cell of
        // such objects reads them through the content. An object of code that cannot know the
        // library, such as a list, has the state of the values that its constructor gets.
        LoopState state =
            this.TypeState(creation)
                .Join(this.Follow(creation)?.State ?? LoopState.None)
                .Join(this.MovedInto(creation))
                .Join(this.MadeByTheLibrary(creation));

        if (initializer != null)
        {
            state = this.WithInitializer(state: state, initializer: initializer);
        }

        // An object of code that cannot know the library is not an object of this compilation.
        // Thus, it contains the objects that have the members, and is not one of them.
        LoopState made = this.MadeHere(
            place: creation,
            state: this.region.SymbolOf(creation) is IMethodSymbol constructor
                   && this.Classify(call: creation, method: constructor) == CalleeKind.Mover
                ? this.WithParts(
                state: state.AsContainer,
                made: constructor.ContainingType,
                callee: constructor,
                arguments: creation switch
                {
                    ObjectCreationExpressionSyntax o => o.ArgumentList?.Arguments ?? default,
                    ImplicitObjectCreationExpressionSyntax i => i.ArgumentList.Arguments,
                    _ => default
                })
            : state,
            exact: true);

        this.NotesTheCollectionsOf(creation: creation, made: made, initializer: initializer);

        return made;
    }

    /// <summary>
    ///     Gives a pair that the code makes a state for each of its parts. The
    ///     state of a part is the state of the argument for the parameter with the
    ///     name of the property.
    /// </summary>
    /// <remarks>
    ///     The pair keeps the state of all that it contains. A path that reads
    ///     the pair as a container uses that state, as a list of pairs does.
    /// </remarks>
    /// <param name="state">The state of all that the pair contains.</param>
    /// <param name="made">The type of the object that the callee makes.</param>
    /// <param name="callee">The constructor, or <c>Create</c>.</param>
    /// <param name="arguments">The arguments of the call.</param>
    private LoopState WithParts(
        LoopState state,
        ITypeSymbol? made,
        IMethodSymbol callee,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        // Most pairs contain nothing with a state. Such a pair gets no state for a part.
        if (made == null || state.IsNone || !SodaFlowSymbols.HoldsParts(made))
        {
            return state;
        }

        Construction parts = Construction.Empty;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (SodaFlowSymbols.ParameterOf(callee: callee.OriginalDefinition, argument: arguments[i], position: i)
                    is { } parameter
                && SodaFlowSymbols.PartFor(holder: made, parameter: parameter) is { } part)
            {
                parts = parts.With(member: part, state: this.Eval(arguments[i].Expression));
            }
        }

        return state.WithMembers(Construction.Merge(first: state.Members, second: parts));
    }

    /// <summary>
    ///     The state of an object after an initializer. An object initializer or
    ///     a with expression sets members, and the state of each replaces the state
    ///     from the constructor. A collection initializer adds elements, and the
    ///     collection gets their states also.
    /// </summary>
    private LoopState WithInitializer(LoopState state, InitializerExpressionSyntax initializer)
    {
        foreach (ExpressionSyntax expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment
                && this.region.SymbolOf(name) is { } member and (IPropertySymbol or IFieldSymbol))
            {
                // A member whose value is an initializer keeps its object, and that object gets
                // the members or the elements of the initializer.
                if (assignment.Right is InitializerExpressionSyntax nested)
                {
                    ISymbol written = this.InitializedThrough(member);

                    state =
                        state.WithMembers(
                            (state.Members ?? Construction.Empty).With(
                                member: written,
                                state: this.WithInitializer(
                                    state: state.Members?.StateThrough(
                                               member: written,
                                               implementations: this.region.Symbols.Implementations)
                                           ?? LoopState.None,
                                    initializer: nested)));

                    continue;
                }

                state =
                    state.WithMembers(
                        (state.Members ?? Construction.Empty).With(member: member, state: this.Eval(assignment.Right)));
            }
            else
            {
                // An element of a collection, or the value for an index.
                state =
                    state.Join(
                        this.Eval(expression is AssignmentExpressionSyntax indexed ? indexed.Right : expression));

                // A setter with no body to read keeps each index also, as the indexer of a
                // dictionary keeps its key.
                if (expression is AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax keyed }
                    && this.region.SymbolOf(keyed) is IPropertySymbol { DeclaringSyntaxReferences.IsEmpty: true })
                {
                    state =
                        state.Join(
                            this.JoinAll(keyed.ArgumentList.Arguments.Select(static index => index.Expression)));
                }
            }
        }

        return state;
    }

    /// <summary>
    ///     The member whose object an initializer of a member writes, as in
    ///     <c>A = { B = x }</c>. For a property whose getter only names a field or
    ///     a property of its object, it is that member. For each other member, it
    ///     is the member itself.
    /// </summary>
    public ISymbol InitializedThrough(ISymbol member)
    {
        if (member is not IPropertySymbol property)
        {
            return member;
        }

        ExpressionSyntax? returned =
            GetterOf(property.OriginalDefinition) switch
            {
                ExpressionSyntax expression => expression,
                BlockSyntax { Statements.Count: 1 } block => (block.Statements[0] as ReturnStatementSyntax)?.Expression,
                _ => null
            };

        return returned is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
               && this.region.SymbolOf(returned) is { IsStatic: false } behind and (IFieldSymbol or IPropertySymbol)
            ? behind
            : member;
    }

    /// <summary>
    ///     What the analyzer knows about the callee of a call to code that is not
    ///     the library.
    /// </summary>
    public CalleeKind Classify(SyntaxNode call, IMethodSymbol method)
    {
        if (!this.kinds.TryGetValue(key: call, value: out CalleeKind kind))
        {
            kind = this.ClassifyUncached(call: call, method: method);
            this.kinds[call] = kind;
        }

        return kind;
    }

    private CalleeKind ClassifyUncached(SyntaxNode call, IMethodSymbol method)
    {
        if (this.region.Symbols.IsSodaFlow(method))
        {
            return CalleeKind.Library;
        }

        if (method.MethodKind == MethodKind.DelegateInvoke || IsDynamicInvoke(method))
        {
            return this.Follow(call) != null ? CalleeKind.Body : CalleeKind.Unknown;
        }

        // Reflection runs code that it finds at run time. The walk reads only the forms that name that
        // code with literals.
        if (SodaFlowSymbols.IsReflectionCall(method))
        {
            return call is InvocationExpressionSyntax reflected
                   && this.ReflectedCall(invocation: reflected, method: method) != null
                   && this.Follow(call) != null
                ? CalleeKind.Body
                : CalleeKind.Unknown;
        }

        IReadOnlyList<IMethodSymbol> definitions =
            call is InvocationExpressionSyntax invocation
                ? this.DefinitionsOf(invocation: invocation, method: method)
                : [method.OriginalDefinition];

        // An override can replace the method, and the walk does not know which one runs.
        if (definitions.Count == 0)
        {
            return CalleeKind.Unknown;
        }

        IMethodSymbol definition = definitions[0];

        bool hasBody =
            definition.MethodKind == MethodKind.Constructor
                ? definition.DeclaringSyntaxReferences.Length > 0
                  || (definition.IsImplicitlyDeclared
                      && definition.ContainingType.DeclaringSyntaxReferences.Length > 0)
                : BodyOf(definition) != null;

        if (hasBody)
        {
            // A body that is too deep is code that the analyzer does not read.
            return this.Follow(call) != null ? CalleeKind.Body : CalleeKind.Unknown;
        }

        return this.region.Symbols.CanKnowTheLibrary(definition.ContainingAssembly)
            ? CalleeKind.Unknown
            : CalleeKind.Mover;
    }

    /// <summary>
    ///     True for a method that is not of the platform. For a callee with no
    ///     body in an assembly that cannot know the library, the method is then
    ///     one of a package. The analyzer has a table of what a method of the
    ///     platform keeps, and no such table for a package.
    /// </summary>
    public static bool IsOfAPackage(IMethodSymbol method) =>
        !SodaFlowSymbols.IsPlatform((method.ReducedFrom ?? method).OriginalDefinition);

    /// <summary>
    ///     The state of the values that a call gives to code that cannot know the
    ///     library. They are the receiver and each argument that is not a function
    ///     for the callee to run. That code can keep them, give them back, or give
    ///     them to a function of the caller.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The callee.</param>
    /// <param name="withReceiver">
    ///     False for only the arguments, which are what the call can put into the
    ///     receiver.
    /// </param>
    /// <param name="withObjects">
    ///     False for only the arguments that are no object of the store. The
    ///     receiver then holds each other argument as an object.
    /// </param>
    public LoopState Moved(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        bool withReceiver,
        bool withObjects = true)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        ExpressionSyntax? receiver = ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        // The receiver of an extension method is the argument of its first parameter. A callee
        // that enumerates a sequence of this compilation gets its elements also.
        LoopState seed =
            !withReceiver
                ? LoopState.None
                : method.ReducedFrom == null
                    ? this.Eval(receiver)
                    : this.Eval(receiver)
                        .Join(this.Enumerated(written: receiver, parameter: definition.Parameters[0].Type));

        return this.MovedArguments(
            definition: definition,
            arguments: invocation.ArgumentList.Arguments,
            offset: method.ReducedFrom != null ? 1 : 0,
            seed: seed,
            withObjects: withObjects);
    }

    private LoopState MovedArguments(
        IMethodSymbol definition,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int offset,
        LoopState seed,
        bool withObjects)
    {
        // A callee that returns a delegate gives back the function that it got, as a wrapper
        // does. Then, the function is a value that it moves. A Lazy and a Task keep the function
        // that their constructor gets. A method that returns a Lazy gives its function to that Lazy.
        // A comparer from a function, as from Comparer.Create, has that function.
        bool keepsFunctions =
            definition.ReturnType.TypeKind == TypeKind.Delegate
            || SodaFlowSymbols.IsLazy(definition.ReturnType)
            || SodaFlowSymbols.IsComparer(definition.ReturnType)
            || (definition.MethodKind == MethodKind.Constructor
                && (SodaFlowSymbols.IsLazy(definition.ContainingType)
                    || SodaFlowSymbols.IsTask(definition.ContainingType)));

        // A function for a delegate parameter is code for the callee to run, and not a value that
        // it moves.
        return arguments
            .Select((argument, position) =>
                (Written: argument.Expression,
                    Parameter: SodaFlowSymbols.ParameterOf(
                        callee: definition,
                        argument: argument,
                        position: position + offset)))
            .Where(given => keepsFunctions || given.Parameter is not { Type.TypeKind: TypeKind.Delegate })
            .Where(given => withObjects || !IsOnlyKnownObjects(this.Eval(given.Written)))
            .Aggregate(
                seed: seed,
                func: (moved, given) =>
                    moved.Join(this.Eval(given.Written))
                        .Join(this.Enumerated(written: given.Written, parameter: given.Parameter?.Type)));
    }

    // A method with no body to read can give back an object that its owner keeps. A result that
    // is a sequence can contain each of those objects. Each other result can be one of them. The
    // result can be an object that the store does not know also.
    private LoopState KeptByTheOwnerOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        // An owner with no object of the store keeps a copy of each value with no object that it
        // got. See KeepsWhatItGets.
        if (method.IsStatic
            || method.ReducedFrom != null
            || invocation.Expression is not MemberAccessExpressionSyntax { Expression: var owner }
            || this.Eval(owner) is not { } has
            || has is not ({ HoldsAnObject: true } or { IsContainer: true, Members: not null }))
        {
            return LoopState.None;
        }

        LoopState kept = has.AsOneOfItsElements;

        return SodaFlowSymbols.IsSequence(method.ReturnType)
            ? kept.Holding(kept.Objects)
                .WithObjects(ImmutableArray<MadeObject>.Empty)
                .AsContainer
                .HoldingAnUnknownObject
            : kept.OrAnUnknownObject;
    }

    /// <summary>
    ///     The location of an exchange, the value that the callee puts there,
    ///     and the comparand of a <c>CompareExchange</c>. Null when the call is
    ///     not an exchange.
    /// </summary>
    public static (ExpressionSyntax Location, ExpressionSyntax Value, ExpressionSyntax? Comparand)? ExchangeOf(
        InvocationExpressionSyntax invocation,
        IMethodSymbol definition)
    {
        if (!SodaFlowSymbols.IsExchange(definition))
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        ExpressionSyntax? location = null;
        ExpressionSyntax? value = null;
        ExpressionSyntax? comparand = null;

        for (int i = 0; i < arguments.Count; i++)
        {
            switch (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i)?.Ordinal)
            {
                case 0:
                    location = arguments[i].Expression;
                    break;
                case 1:
                    value = arguments[i].Expression;
                    break;
                case 2:
                    comparand = arguments[i].Expression;
                    break;
            }
        }

        return location != null && value != null ? (location, value, comparand) : null;
    }

    /// <summary>
    ///     The result of a call to code that cannot know the library, where the
    ///     type of the result can hold a state. The result has the state of the
    ///     values that the callee got, and of what a lambda that it runs returns.
    /// </summary>
    private LoopState MovedResult(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        // A sequence of numbers has no state, but it can be or contain a query. That query is
        // one that the callee got, or one that a function of the callee returns.
        bool holdsState =
            this.region.Symbols.CanHoldState(type: method.ReturnType, compilation: this.region.Assembly);

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        // Reflection makes an object or calls a method that the analyzer cannot follow. Its
        // result is not one of the values that it got.
        if ((!holdsState && !SodaFlowSymbols.CanHoldQuery(method.ReturnType))
            || SodaFlowSymbols.IsReflection(definition))
        {
            return LoopState.None;
        }

        if (ExchangeOf(invocation: invocation, definition: definition) is { } exchange)
        {
            return this.Eval(exchange.Location);
        }

        LoopState moved = this.Moved(invocation: invocation, method: method, withReceiver: true);
        LoopState result = moved;
        LoopState element = moved.AsElement;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression is AnonymousFunctionExpressionSyntax lambda
                && RunsFunction(
                    kind: CalleeKind.Mover,
                    method: method,
                    parameter: ParameterFor(method: method, argument: arguments[i], position: i)))
            {
                result =
                    result.Join(
                        this.EvalLambdaResult(
                            lambda: lambda,
                            boundParameters: ParametersOf(lambda).Select(_ => element).ToImmutableArray()));
            }
        }

        if (!holdsState)
        {
            return result.OnlyQueries;
        }

        // A callee that has a sequence gives one of its values, as First does. A callee with no
        // sequence gives back what it got.
        bool takesOneOut =
            this.region.Symbols.TakesASequence(definition)
            || (!definition.IsStatic && SodaFlowSymbols.IsEnumerated(definition.ContainingType));

        LoopState returned = Returned(state: result, type: method.ReturnType, takesOneOut: takesOneOut);

        if (!takesOneOut || SodaFlowSymbols.IsSequence(method.ReturnType))
        {
            return returned;
        }

        // A method that gives one value of a sequence can give back an argument also, as
        // FirstOrDefault gives its default value. The result can then be the object of that
        // argument.
        ImmutableArray<MadeObject> given =
            this.Moved(invocation: invocation, method: method, withReceiver: false)
                .Objects
                .RemoveAll(made => returned.Objects.Contains(made));

        return given.IsEmpty ? returned : returned.WithObjects(returned.Objects.AddRange(given));
    }

    /// <summary>
    ///     The state of what a method of code that cannot know the library
    ///     returns, from the state of the values that it got. A result that is a
    ///     sequence contains those values. Each other result can be one of them, as
    ///     the result of <c>First</c> is.
    /// </summary>
    /// <param name="state">The state of the values that the method got.</param>
    /// <param name="type">The type of the result.</param>
    /// <param name="takesOneOut">
    ///     True when the receiver or a parameter of the method is a sequence. A
    ///     result whose type says nothing is then one value of that sequence. False
    ///     for a method that can only give back what it got. A list that it got as
    ///     <c>object</c> stays a list.
    /// </param>
    private static LoopState Returned(LoopState state, ITypeSymbol type, bool takesOneOut)
    {
        if (!SodaFlowSymbols.IsSequence(type))
        {
            return takesOneOut || !Construction.SaysNothing(type) ? state.AsOneOfItsElements : state;
        }

        // A method can give back a sequence that it got, as AsEnumerable does. That sequence can
        // be an object of this compilation, which has its own members.
        return state.Members is { } members && (!members.HasType || members.Is(type)) ? state : state.AsContainer;
    }

    /// <summary>
    ///     True when the callee runs, in the call, the function that it gets for
    ///     the given parameter.
    /// </summary>
    /// <remarks>
    ///     A library method says so in <see cref="OperatorTable" />. A callee
    ///     whose body the evaluator follows gets the function as a value, and the
    ///     body says what runs. For code that the analyzer cannot read, only the
    ///     call itself can tell. A constructor keeps a function for a time
    ///     after the call, as <c>Lazy</c>, a task, and a command object do. A
    ///     method runs a function given for a delegate parameter. It does not when
    ///     it returns a delegate or a <c>Lazy</c>, which then has the function. A
    ///     function given for a parameter of a different type, as to <c>Add</c> of
    ///     a list, is a value that the callee keeps. A read of <c>Value</c> runs
    ///     the function that a <c>Lazy</c> has, which <see cref="FollowLazy" />
    ///     follows. <c>RunSynchronously</c> runs the function of a task. A
    ///     comparer is a function with a different type. A method runs one that it
    ///     gets, and a method that returns one keeps the function that makes it.
    /// </remarks>
    public static bool RunsFunction(CalleeKind kind, IMethodSymbol method, IParameterSymbol? parameter) =>
        kind switch
        {
            CalleeKind.Library => OperatorTable.RunsFunction(method: method, parameter: parameter),
            CalleeKind.Body => false,
            _ => method.MethodKind != MethodKind.Constructor
                 && parameter != null
                 && (parameter.Type.TypeKind == TypeKind.Delegate || SodaFlowSymbols.IsComparer(parameter.Type))
                 && method.ReturnType.TypeKind != TypeKind.Delegate
                 && !SodaFlowSymbols.IsLazy(method.ReturnType)
                 && !SodaFlowSymbols.IsComparer(method.ReturnType)
                 && !RunsOnAnotherThread(method.ContainingType)
        };

    /// <summary>
    ///     True for <c>RunSynchronously</c> of a task, which runs the function
    ///     of the task in the call.
    /// </summary>
    private static bool IsRunSynchronously(ISymbol method) =>
        method.Name == "RunSynchronously" && SodaFlowSymbols.IsTask(method.ContainingType);

    private static bool IsDynamicInvoke(IMethodSymbol method) =>
        method is
        {
            Name: "DynamicInvoke", Parameters.Length: 1, ContainingType.SpecialType: SpecialType.System_Delegate
        };

    /// <summary>
    ///     The parameter that gets an argument of a call. It is a parameter of the
    ///     definition of the callee, which has the types that the callee itself
    ///     knows. For a call of a delegate, it is a parameter of that delegate.
    /// </summary>
    public static IParameterSymbol? ParameterFor(IMethodSymbol method, ArgumentSyntax argument, int position) =>
        method.MethodKind == MethodKind.DelegateInvoke
            ? SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position)
            : SodaFlowSymbols.ParameterOf(
                callee: (method.ReducedFrom ?? method).OriginalDefinition,
                argument: argument,
                position: position + (method.ReducedFrom != null ? 1 : 0));

    /// <summary>
    ///     The type that a callee knows for an argument: the type of the parameter
    ///     of its definition. For a <c>params</c> parameter that gets its
    ///     arguments one by one, it is the type of one element. The callee then
    ///     has the argument as an element, and not as the sequence.
    /// </summary>
    public ITypeSymbol? KnownTypeFor(IMethodSymbol method, ArgumentSyntax argument, int position)
    {
        IParameterSymbol? parameter = ParameterFor(method: method, argument: argument, position: position);

        if (parameter is not { IsParams: true } || argument.Parent is not BaseArgumentListSyntax list)
        {
            return parameter?.Type;
        }

        // The parameter of the call has the types of this call. The compiler gives the arguments
        // one by one when they are more than the parameters. It does that also for one argument
        // that it converts to an element, and not to the type of the parameter.
        IParameterSymbol own = method.Parameters[method.Parameters.Length - 1];

        bool oneByOne =
            list.Arguments.Count > method.Parameters.Length
            || (this.region.TypeOf(argument.Expression) is { } given
                && !this.region.Converts(from: given, to: own.Type));

        return oneByOne
            ? parameter.Type switch
            {
                IArrayTypeSymbol array => array.ElementType,
                INamedTypeSymbol { TypeArguments.Length: 1 } collection => collection.TypeArguments[0],
                _ => parameter.Type
            }
            : parameter.Type;
    }

    /// <summary>
    ///     A type whose lambdas run on a different thread or after the block, so
    ///     not in the block.
    /// </summary>
    private static bool RunsOnAnotherThread(ISymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() is "System.Threading" or "System.Threading.Tasks"
        && type.Name is "Thread" or "Task" or "TaskFactory" or "ThreadPool" or "Timer" or "SynchronizationContext";

    /// <summary>
    ///     The state of an object from a constructor of code that cannot know the
    ///     library: the state of the arguments.
    /// </summary>
    /// <summary>
    ///     An object that a constructor of the library makes. It has the state
    ///     of its arguments, as the result of an operator with no row has, when its
    ///     type can hold a pending read.
    /// </summary>
    private LoopState MadeByTheLibrary(SyntaxNode creation) =>
        creation is BaseObjectCreationExpressionSyntax { ArgumentList: { } arguments }
        && this.region.SymbolOf(creation) is IMethodSymbol constructor
        && this.Classify(call: creation, method: constructor) == CalleeKind.Library
        && this.region.Symbols.IsCarrierType(constructor.ContainingType)
            ? arguments.Arguments.Aggregate(
                    seed: LoopState.None,
                    func: (all, argument) => all.Join(this.EvalArgument(argument: argument.Expression, runsNow: false)))
                .Join(
                    this.TransformedByTheConstructor(
                        creation: creation,
                        arguments: arguments,
                        constructor: constructor))
            : LoopState.None;

    private LoopState MovedInto(SyntaxNode creation)
    {
        BaseArgumentListSyntax? arguments =
            creation switch
            {
                ObjectCreationExpressionSyntax written => written.ArgumentList,
                ImplicitObjectCreationExpressionSyntax implied => implied.ArgumentList,
                _ => null
            };

        return arguments != null
               && this.region.SymbolOf(creation) is IMethodSymbol constructor
               && this.Classify(call: creation, method: constructor) == CalleeKind.Mover
            ? this.MovedArguments(
                definition: constructor.OriginalDefinition,
                arguments: arguments.Arguments,
                offset: 0,
                seed: LoopState.None,
                withObjects: true)
            : LoopState.None;
    }

    private Followed? FollowInvocation(InvocationExpressionSyntax invocation)
    {
        if (this.region.SymbolOf(invocation) is not IMethodSymbol method)
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        // Reflection runs the method or the constructor that a literal names.
        if (SodaFlowSymbols.IsReflectionCall(method)
            && this.ReflectedCall(invocation: invocation, method: method) is { } reflected)
        {
            return this.Wrapped(
                followed: this.FollowReflection(invocation: invocation, reflected: reflected),
                wrapper: "System.Reflection.TargetInvocationException");
        }

        // A call of a delegate runs the functions that the delegate can be.
        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            ExpressionSyntax target = this.DelegateOf(invocation);
            LoopState targetState = this.Eval(target);

            return targetState.Functions.IsEmpty
                ? null
                : ForTheParametersOf(
                    invoke: method,
                    followed: this.FollowFunctions(
                        call: invocation,
                        name: NameOf(target),
                        functions: targetState.Functions,
                        target: targetState,
                        arguments: arguments));
        }

        // DynamicInvoke runs the functions of the delegate also. Each argument at the call is one
        // argument of the function. An array at the call is the arguments themselves, and the
        // walk does not know them.
        if (IsDynamicInvoke(method))
        {
            return invocation.Expression is MemberAccessExpressionSyntax access
                   && this.Eval(access.Expression) is { Functions.IsEmpty: false } functions
                   && arguments.All(argument => this.IsOneOfAParamsArray(
                       parameter: method.Parameters[0],
                       argument: argument.Expression))
                ? this.FollowFunctions(
                    call: invocation,
                    name: NameOf(access.Expression),
                    functions: functions.Functions,
                    target: functions,
                    arguments: arguments)
                : null;
        }

        // An anonymous type has an Equals, a GetHashCode, and a ToString that the compiler writes. The
        // symbol of a call of one is the member of object.
        if (method is
            {
                ContainingType.SpecialType: SpecialType.System_Object,
                Name: "Equals" or "GetHashCode" or "ToString"
            }
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: var receiver }
            && this.Self(receiver) is { Members.IsAnonymous: true } anonymous)
        {
            return this.FollowImplied(
                call: invocation,
                member: method,
                owner: anonymous,
                arguments: [.. arguments.Select(argument => (argument.Expression, this.Eval(argument.Expression)))]);
        }

        if (this.region.Symbols.IsSodaFlow(method))
        {
            return OperatorTable.FunctionsInTheCall.Contains(OperatorName(method))
                ? this.FollowGivenFunctions(invocation: invocation, method: method)
                : null;
        }

        if (method.Name == "GetResult" && this.FaultOfATask(invocation: invocation, method: method) is { } fault)
        {
            return fault;
        }

        // RunSynchronously runs the function of a task in the call, as a read of Value does for a
        // Lazy. Start and Task.Run run it on a different thread. The task holds the state object
        // of its constructor, and the function gets that object as its parameter.
        if (IsRunSynchronously(method)
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: var task })
        {
            return KeptByTheTask(
                this.FollowLazy(
                    access: invocation,
                    written: task,
                    lazy: this.Eval(task).AsOneOfItsElements,
                    value: method.ReturnType));
        }

        // A call on a value that is one of two objects can run the method of each.
        IMethodSymbol[] definitions =
        [
            .. this.DefinitionsOf(invocation: invocation, method: method)
                .Where(static candidate => BodyOf(candidate) != null)
        ];

        return definitions.Length == 0
            ? this.FollowGivenFunctions(invocation: invocation, method: method)
            : definitions.Aggregate(
                seed: (Followed?)null,
                func: (all, definition) =>
                    Both(
                        first: all,
                        second: this.FollowDefinition(
                            invocation: invocation,
                            method: method,
                            definition: definition)));
    }

    // A call of a delegate names the parameters of the delegate, and the function has parameters of
    // its own. The caller gives each argument what the function set through the parameter at the
    // same position, as for a call of a method. A method of a delegate with a receiver has a
    // parameter for the receiver, and keeps its own parameters.
    private static Followed? ForTheParametersOf(IMethodSymbol invoke, Followed? followed)
    {
        if (followed == null || (followed.Outs.IsEmpty && followed.Through.IsEmpty && followed.AlwaysGives.IsEmpty))
        {
            return followed;
        }

        ImmutableArray<IParameterSymbol> parameters = invoke.OriginalDefinition.Parameters;

        // Two functions that the delegate can be give two states for one parameter.
        ImmutableDictionary<ISymbol, LoopState>.Builder outs =
            ImmutableDictionary.CreateBuilder<ISymbol, LoopState>(SymbolEqualityComparer.Default);

        foreach (KeyValuePair<ISymbol, LoopState> pair in followed.Outs)
        {
            ISymbol key = Of(pair.Key) ?? pair.Key;
            outs[key] = outs.TryGetValue(key: key, value: out LoopState? other) ? other.Join(pair.Value) : pair.Value;
        }

        return new Followed(
            state: followed.State,
            findings: followed.Findings,
            outs: outs.ToImmutable(),
            sets: followed.Sets,
            through: followed.Through
                .Select(one => (Of(one.Parameter) ?? one.Parameter, one.Member, one.State))
                .ToImmutableArray(),
            alwaysGives: followed.AlwaysGives
                .Select(one => Of(one) ?? one)
                .ToImmutableHashSet(SymbolEqualityComparer.Default),
            thrown: followed.Thrown);

        IParameterSymbol? Of(ISymbol parameter) =>
            parameter is IParameterSymbol { ContainingSymbol: IMethodSymbol { IsExtensionMethod: false } } own
            && own.Ordinal < parameters.Length
                ? parameters[own.Ordinal]
                : null;
    }

    private Followed? FollowDefinition(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IMethodSymbol definition)
    {
        if (BodyOf(definition) is not { } body)
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        ExpressionSyntax? receiver =
            ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        IReadOnlyList<Binding> bindings =
            this.Bind(
                definition: definition,
                arguments: arguments,
                extensionReceiver: method.ReducedFrom != null ? receiver : null);

        IParameterSymbol? extended = receiver == null ? null : this.ExtensionParameterOf(definition);

        if (extended != null && receiver != null)
        {
            bindings = [new Binding(parameter: extended, argument: receiver, state: this.Eval(receiver)), .. bindings];
        }

        // A local function is part of the code that contains it, and reads the locals of that
        // code.
        if (definition.MethodKind == MethodKind.LocalFunction)
        {
            return this.Enter(
                call: invocation,
                name: method.Name,
                body: body,
                bindings: bindings,
                definer: this,
                members: this.thisMembers,
                receiver: null,
                target: null);
        }

        // The receiver of a member of an extension block is a parameter, and not this object.
        (Dictionary<ISymbol, LoopState> members, LoopState? receiverState) =
            this.MembersFor(isStatic: definition.IsStatic || extended != null, owner: receiver);

        return this.Enter(
            call: invocation,
            name: method.Name,
            body: body,
            bindings: bindings,
            definer: null,
            members: members,
            receiver: receiverState,
            target: null,
            returnsAFunction: definition.ReturnType.TypeKind == TypeKind.Delegate,
            returnsAnObject: this.DoesNotSayItsClass(this.TypeArgumentFor(method.ReturnType) ?? method.ReturnType),
            owner: receiverState == null ? null : receiver);
    }

    /// <summary>
    ///     The definition of the method that a call runs. The call can be through
    ///     an interface or a base type, on an object whose constructor the evaluator
    ///     followed. Then, it runs the method of the type of that object. A value
    ///     that is one of two objects gives the method of each. Empty for an
    ///     object that the evaluator did not see made, when an override can run.
    /// </summary>
    private IReadOnlyList<IMethodSymbol> DefinitionsOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        // A static member of an interface has no body when it is abstract. A call through a type
        // parameter runs the member of the type argument that the call of this code gave.
        if (definition is { IsStatic: true, ContainingType.TypeKind: TypeKind.Interface }
            && (definition.IsAbstract || definition.IsVirtual))
        {
            return [this.ImplementationThroughATypeArgument(invocation: invocation, method: method) ?? definition];
        }

        if (method is not { IsStatic: false, ReducedFrom: null })
        {
            return [definition];
        }

        ExpressionSyntax? receiver = ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        if (receiver is null or ThisExpressionSyntax
            && this.ImplementationsOnThis(definition).OfType<IMethodSymbol>().ToArray() is { Length: > 0 } onThis)
        {
            return [.. onThis.Select(static found => found.OriginalDefinition)];
        }

        // A call with no receiver is a call on this.
        if ((receiver is null or ThisExpressionSyntax
                ? this.builtType != null ? Construction.Of(this.builtType) : this.thisObject
                : this.Self(receiver).Members) is not { } members)
        {
            return !this.CanRunAnOverride(receiver: receiver, definition: definition, call: invocation)
                ? [definition]
                : receiver is null or ThisExpressionSyntax
                    ? this.OnEachClassOfThis(definition: definition, call: invocation)
                    : [];
        }

        IMethodSymbol[] actual =
        [
            .. members.Resolve(definition).OfType<IMethodSymbol>().Select(static found => found.OriginalDefinition)
        ];

        return actual.Length == 0 ? [definition] : actual;
    }

    // The member of the type argument that implements a static member of an interface, for a call
    // through a type parameter. The interface can be generic, thus the walk finds it in the
    // interfaces of the type argument. Null when the call of this code does not say the type.
    private IMethodSymbol? ImplementationThroughATypeArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access
            || this.TypeArgumentFor(this.region.TypeOf(access.Expression)) is not INamedTypeSymbol actual)
        {
            return null;
        }

        IMethodSymbol definition = method.OriginalDefinition;

        IMethodSymbol? member =
            actual.AllInterfaces
                .Where(candidate => SymbolEqualityComparer.Default.Equals(
                    x: candidate.OriginalDefinition,
                    y: definition.ContainingType))
                .SelectMany(candidate => candidate.GetMembers(definition.Name).OfType<IMethodSymbol>())
                .FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(x: candidate.OriginalDefinition, y: definition));

        return member != null && actual.FindImplementationForInterfaceMember(member) is IMethodSymbol found
            ? found.OriginalDefinition
            : null;
    }

    /// <summary>
    ///     A call to code with no body to read that runs a function in the call.
    ///     The walk reads a lambda written at the call where it is. Each other
    ///     function, such as a local function or a lambda in a local, gets a frame
    ///     as a call of it does. Each parameter of that function has the state of
    ///     the values that the callee can give it.
    /// </summary>
    private Followed? FollowGivenFunctions(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        CalleeKind kind =
            this.region.Symbols.IsSodaFlow(method)
                ? CalleeKind.Library
                : this.region.Symbols.CanKnowTheLibrary(method.ContainingAssembly)
                    ? CalleeKind.Unknown
                    : CalleeKind.Mover;

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        Followed? all = null;

        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax written = arguments[i].Expression;

            IParameterSymbol? parameter = ParameterFor(method: method, argument: arguments[i], position: i);

            if (written is AnonymousFunctionExpressionSyntax
                || !RunsFunction(kind: kind, method: method, parameter: parameter)
                || this.WaitsBeforeItsFunctions(call: invocation, method: method))
            {
                continue;
            }

            LoopState functions = this.Eval(written);
            ImmutableArray<FunctionValue> callables = Callables(state: functions, parameter: parameter);

            if (callables.IsEmpty)
            {
                continue;
            }

            LoopState given =
                kind == CalleeKind.Mover
                    ? this.Moved(invocation: invocation, method: method, withReceiver: true).AsElement
                    : LoopState.None;

            // A transaction and a loop block run the function one time, as a call of it does.
            bool manyTimes = kind != CalleeKind.Library || !OperatorTable.RunsFunctionOneTime(method);

            Followed? ofArgument =
                callables.Aggregate(
                    seed: (Followed?)null,
                    func: (followed, function) =>
                        Both(
                            first: followed,
                            second: this.FollowFunction(
                                call: invocation,
                                name: NameOf(written),
                                function: function,
                                target: functions,
                                arguments: default,
                                each: (written, given, ManyTimes: manyTimes))));

            if (ofArgument != null)
            {
                this.functionResults[written] = ofArgument.State;
            }

            all = Both(first: all, second: ofArgument);
        }

        return all;
    }

    /// <summary>
    ///     The state of what a function returns, for a function that is not a
    ///     lambda and that a callee with no body to read runs. The walk of the
    ///     call must come first.
    /// </summary>
    /// <param name="written">The argument that names the function at the call.</param>
    public LoopState ResultOfFunction(ExpressionSyntax written) =>
        this.functionResults.TryGetValue(key: written, value: out LoopState? result) ? result : LoopState.None;

    /// <summary>
    ///     The functions that a callee can run through an argument. They are the
    ///     functions that the argument can be. For a comparer that is an object of
    ///     this compilation, they are also the methods of the comparer, which the
    ///     callee calls through the interface.
    /// </summary>
    private static ImmutableArray<FunctionValue> Callables(LoopState state, IParameterSymbol? parameter) =>
        state.Members == null || !SodaFlowSymbols.IsComparer(parameter?.Type)
            ? state.Functions
            : state.Functions.AddRange(
                state.Members.Implementations(SodaFlowSymbols.IsComparerInterface)
                    .Select(static method => method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax())
                    .OfType<MethodDeclarationSyntax>()
                    .Select(declaration =>
                        new FunctionValue(declaration: declaration, definer: null, receiver: state.Members)));

    private static Followed? Both(Followed? first, Followed? second) =>
        first == null
            ? second
            : second == null
                ? first
                : new Followed(
                    state: first.State.Join(second.State),
                    findings: first.Findings.AddRange(second.Findings),
                    outs: first.Outs.SetItems(second.Outs),
                    sets: first.Sets.AddRange(second.Sets),
                    through: first.Through.AddRange(second.Through),
                    alwaysGives: first.AlwaysGives.Intersect(second.AlwaysGives),
                    thrown: first.Thrown.AddRange(second.Thrown));

    private Followed? FollowFunctions(
        SyntaxNode call,
        string name,
        ImmutableArray<FunctionValue> functions,
        LoopState target,
        SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        functions.Aggregate(
            seed: (Followed?)null,
            func: (all, function) =>
                Both(
                    first: all,
                    second: this.FollowFunction(
                        call: call,
                        name: name,
                        function: function,
                        target: target,
                        arguments: arguments,
                        each: null)));

    /// <param name="call">The call that runs the function.</param>
    /// <param name="name">The name of the function, for a report.</param>
    /// <param name="function">The function.</param>
    /// <param name="target">The delegate that the function is a value of.</param>
    /// <param name="arguments">The arguments, when the call is a call of the delegate.</param>
    /// <param name="each">
    ///     For a callee with no body that runs the function. It is the expression
    ///     that names the function at the call, and the state that each of its
    ///     parameters gets. The last part is true when the callee can run the
    ///     function two or more times in the call.
    /// </param>
    /// <param name="byPosition">
    ///     For a callee with no body, the state that each parameter gets at its
    ///     position, in place of the state of <paramref name="each" />.
    /// </param>
    private Followed? FollowFunction(
        SyntaxNode call,
        string name,
        FunctionValue function,
        LoopState target,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        (ExpressionSyntax Written, LoopState Given, bool ManyTimes)? each,
        ImmutableArray<LoopState> byPosition = default)
    {
        SemanticModel model = this.region.ModelFor(function.Declaration);

        IReadOnlyList<Binding> Bindings(ImmutableArray<IParameterSymbol> parameters) =>
            each is (var written, var given, _)
                ?
                [
                    .. parameters.Select((parameter, position) =>
                        new Binding(
                            parameter: parameter,
                            argument: written,
                            state: byPosition.IsDefault
                                ? given
                                : position < byPosition.Length
                                    ? byPosition[position]
                                    : LoopState.None))
                ]
                : this.BindByPosition(parameters: parameters, arguments: arguments);

        switch (function.Declaration)
        {
            case AnonymousFunctionExpressionSyntax lambda when function.Definer != null:
                return this.Enter(
                    call: call,
                    name: name,
                    body: lambda.Body,
                    bindings: Bindings(
                        ParametersOf(lambda)
                            .Select(parameter => model.GetDeclaredSymbol(parameter))
                            .OfType<IParameterSymbol>()
                            .ToImmutableArray()),
                    definer: function.Definer,
                    members: function.Definer.thisMembers,
                    receiver: null,
                    target: target,
                    manyTimes: each is { ManyTimes: true });
            // A lambda in the initializer of a member. It has no code around it, thus it reads
            // no local.
            case AnonymousFunctionExpressionSyntax lambda:
                return this.Enter(
                    call: call,
                    name: name,
                    body: lambda.Body,
                    bindings: Bindings(
                        ParametersOf(lambda)
                            .Select(parameter => model.GetDeclaredSymbol(parameter))
                            .OfType<IParameterSymbol>()
                            .ToImmutableArray()),
                    definer: null,
                    members: TaggedMembers(LoopState.None),
                    receiver: null,
                    target: null,
                    returnsAnObject: model.GetSymbolInfo(lambda).Symbol is IMethodSymbol { ReturnType: { } returned }
                                     && this.DoesNotSayItsClass(returned),
                    manyTimes: each is { ManyTimes: true });
            case LocalFunctionStatementSyntax or MethodDeclarationSyntax
                when model.GetDeclaredSymbol(function.Declaration) is IMethodSymbol method
                     && BodyOf(method) is { } body:
            {
                IReadOnlyList<Binding> bindings = Bindings(method.Parameters);

                if (function.Definer != null)
                {
                    return this.Enter(
                        call: call,
                        name: name,
                        body: body,
                        bindings: bindings,
                        definer: function.Definer,
                        members: function.Definer.thisMembers,
                        receiver: null,
                        target: target,
                        manyTimes: each is { ManyTimes: true });
                }

                LoopState owner = LoopState.None.WithMembers(function.Receiver);

                return Both(
                    first: this.FollowTheStaticConstructorAtACallOf(call: call, method: method),
                    second: this.Enter(
                        call: call,
                        name: name,
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: TaggedMembers(owner),
                        receiver: owner,
                        target: null,
                        returnsAnObject: this.DoesNotSayItsClass(method.ReturnType),
                        manyTimes: each is { ManyTimes: true }));
            }
            default:
                return null;
        }
    }

    private Followed? FollowCreation(SyntaxNode creation, BaseArgumentListSyntax? arguments) =>
        (this.region.SymbolOf(creation) as IMethodSymbol ?? this.ConstructorOfATypeArgument(creation))
        is { } constructor
        && !this.region.Symbols.IsSodaFlow(constructor)
            ? this.FollowConstructor(
                call: creation,
                constructor: constructor,
                arguments: arguments?.Arguments ?? default)
            : null;

    /// <summary>
    ///     The constructor that the creation of a type parameter runs, when the
    ///     call of this code says which class the type parameter is. Such a
    ///     creation has no arguments.
    /// </summary>
    public IMethodSymbol? ConstructorOfATypeArgument(SyntaxNode creation) =>
        creation is ExpressionSyntax made
        && this.TypeArgumentFor(this.region.TypeOf(made)) is INamedTypeSymbol actual
            ? actual.InstanceConstructors.FirstOrDefault(static constructor => constructor.Parameters.IsEmpty)
            : null;

    // The type that the call of this code gave a type parameter of its method. Null for each other
    // type.
    private ITypeSymbol? TypeArgumentFor(ITypeSymbol? type) =>
        type is ITypeParameterSymbol parameter
        && this.call != null
        && this.call.TypeArguments.TryGetValue(key: parameter, value: out ITypeSymbol? actual)
            ? actual
            : null;

    // The location of a parameter with ref, out, or in of a callee, for a call in this code. The
    // result has the locations of the parameters before it also. The argument is a location that
    // this code can read again, or such a parameter of this code that has a location. The argument
    // for a parameter with in is a copy when it has a different type, and a copy is no location.
    private Locations WithTheLocationFor(Locations all, Binding binding)
    {
        if (binding.Parameter.RefKind == RefKind.None
            || (binding.Parameter.RefKind is not (RefKind.Ref or RefKind.Out)
                && !SymbolEqualityComparer.Default.Equals(
                    x: this.region.TypeOf(binding.Argument),
                    y: binding.Parameter.Type)))
        {
            return all;
        }

        if (Unwrapped(binding.Argument) is IdentifierNameSyntax given
            && this.region.SymbolOf(given) is IParameterSymbol { RefKind: not RefKind.None } outer)
        {
            // The callee gets the location that the parameter of this code has, while this code
            // gave that parameter no value.
            return this.call != null
                   && !this.assigned.Contains(outer)
                   && this.call.Locations.TryGetValue(
                       key: outer,
                       value: out (LoopStateEvaluator Reader, ExpressionSyntax Location) known)
                ? all.SetItem(key: binding.Parameter, value: known)
                : all;
        }

        // A local with ref gives the callee its location.
        if (this.TheLocationOf(binding.Argument) is { } location)
        {
            return all.SetItem(key: binding.Parameter, value: (this, location));
        }

        return this.IsALocationThatTheWalkCanReadAgain(binding.Argument)
            ? all.SetItem(key: binding.Parameter, value: (this, binding.Argument))
            : all;
    }

    /// <summary>
    ///     The state that the location of a parameter with ref, out, or in has
    ///     now, read in the code of the caller. Null for each other expression,
    ///     and when the parameter has the state that it got in the callee.
    /// </summary>
    /// <remarks>
    ///     The parameter has the object of its argument until code gives the
    ///     location a value. That code is not always an assignment to the
    ///     parameter: a second name of the array can give its element an object.
    ///     Thus, the callee reads the location at each use. An assignment of the
    ///     callee to the parameter gives the location the value of the callee. A
    ///     location with no object of the store keeps its members in the names,
    ///     and the read gives nothing more.
    /// </remarks>
    private LoopState? ReadInTheCaller(ExpressionSyntax written)
    {
        if (this.call == null
            || this.call.Locations.IsEmpty
            || Unwrapped(written) is not IdentifierNameSyntax use
            || this.region.SymbolOf(use) is not IParameterSymbol parameter
            || !this.call.Locations.TryGetValue(
                key: parameter,
                value: out (LoopStateEvaluator Reader, ExpressionSyntax Location) of)
            || this.assigned.Contains(parameter))
        {
            return null;
        }

        LoopState now = of.Reader.EvalAgain(of.Location);

        return now.Objects.IsEmpty ? null : now.From(parameter);
    }

    // True for an owner that is a parameter with ref, out, or in, or a member of one. The walk
    // must read the location of that parameter in the code of the caller.
    private bool IsReadInTheCaller(ExpressionSyntax owner)
    {
        ExpressionSyntax written = Unwrapped(owner);

        while (written is MemberAccessExpressionSyntax access)
        {
            written = Unwrapped(access.Expression);
        }

        return this.ReadInTheCaller(written) != null;
    }

    /// <summary>
    ///     Records that a collection got a value of which the walk has only the
    ///     state. A callee that gives a value to a parameter with ref or out gives
    ///     it to the element that is the argument.
    /// </summary>
    /// <param name="collection">The expression of the collection.</param>
    /// <param name="put">The state of the value.</param>
    public void GetsAState(ExpressionSyntax collection, LoopState put)
    {
        // A value that the store does not know comes in.
        if (put.Objects.IsEmpty || put.Objects.Contains(MadeObject.Unknown))
        {
            this.CanHoldMore(collection);
        }

        this.Change(
            owner: this.Eval(collection),
            change: old => put.IsOfNoObject
                ? old.HoldingWhatCameWith(put).HoldingAnUnknownObject
                : old.HoldingWhatCameWith(put),
            replaces: false,
            turn: null);
    }

    // The type that a call gives each type parameter of its method. A type parameter of this code
    // in the call has the type that the call of this code gave it. A lambda or a local function
    // has the type parameters of the code that it is in also.
    private ImmutableDictionary<ISymbol, ITypeSymbol> TypeArgumentsOf(SyntaxNode call, LoopStateEvaluator? definer)
    {
        ImmutableDictionary<ISymbol, ITypeSymbol> all = definer?.call?.TypeArguments ?? NoTypeArguments;

        if (this.region.SymbolOf(call) is not IMethodSymbol method)
        {
            return all;
        }

        all = this.WithTypeArguments(
            all: all,
            parameters: (method.ReducedFrom ?? method).OriginalDefinition.TypeParameters,
            given: method.TypeArguments);

        // A member of a generic type gets the type arguments of that type, as in Factory<T>.Make().
        for (INamedTypeSymbol? type = method.ContainingType;
             type is { IsGenericType: true };
             type = type.ContainingType)
        {
            all = this.WithTypeArguments(
                all: all,
                parameters: type.OriginalDefinition.TypeParameters,
                given: type.TypeArguments);
        }

        return all;
    }

    // The type arguments with the type that each parameter gets.
    private ImmutableDictionary<ISymbol, ITypeSymbol> WithTypeArguments(
        ImmutableDictionary<ISymbol, ITypeSymbol> all,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> given)
    {
        for (int i = 0; i < parameters.Length && i < given.Length; i++)
        {
            all = all.SetItem(key: parameters[i], value: this.TypeArgumentFor(given[i]) ?? given[i]);
        }

        return all;
    }

    /// <summary>
    ///     The constructor that a constructor calls first: the constructor of the
    ///     base, or a second constructor of the type. The members that it sets are
    ///     members of the object that this code is in.
    /// </summary>
    private Followed? FollowChain(SyntaxNode chain, BaseArgumentListSyntax? arguments) =>
        this.region.SymbolOf(chain) is IMethodSymbol constructor && !this.region.Symbols.IsSodaFlow(constructor)
            ? this.FollowChainTo(chain: chain, constructor: constructor, arguments: arguments?.Arguments ?? default)
            : null;

    private Followed? FollowChainTo(
        SyntaxNode chain,
        IMethodSymbol constructor,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        Followed? first =
            this.FollowConstructor(call: chain, constructor: constructor, arguments: arguments, chained: true);

        if (first?.State.Members != null)
        {
            foreach (KeyValuePair<ISymbol, LoopState> pair in first.State.Members.Members)
            {
                this.SetMember(member: pair.Key, state: pair.Value);
            }
        }

        return first;
    }

    // A with expression has no argument list. The record that it copies is the argument of the
    // copy constructor.
    private Followed? FollowConstructor(
        SyntaxNode call,
        IMethodSymbol constructor,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ExpressionSyntax? copied = null,
        bool chained = false,
        IReadOnlyList<Binding>? bound = null)
    {
        IMethodSymbol definition = constructor.OriginalDefinition;
        SyntaxNode? declaration = definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        // A primary constructor declares on its type. An explicit one declares in it. A type with
        // no constructor gets one from the compiler, which runs the initializers of the type.
        TypeDeclarationSyntax? type =
            declaration as TypeDeclarationSyntax
            ?? declaration?.Parent as TypeDeclarationSyntax
            ?? (definition.IsImplicitlyDeclared
                ? definition.ContainingType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    as TypeDeclarationSyntax
                : null);

        if (type == null)
        {
            return null;
        }

        IReadOnlyList<Binding> bindings =
            bound ?? this.Bind(definition: definition, arguments: arguments, extensionReceiver: copied);

        // A constructor that gets no state gives no member a state, and the evaluator does not
        // read it. The object keeps its type, because the code can set a member of it later.
        // A constructor that gets an object of the store can keep it, which gives the object a
        // second name. The evaluator reads such a constructor while ReadsForObjects permits.
        bool noState = !this.region.HasFields && bindings.All(static binding => binding.State.IsNone);

        switch (noState)
        {
            case true when !this.ReadsForObjects(bindings) && !this.ReadsForThis(definition):
                return new Followed(
                    state: LoopState.None.WithMembers(Construction.Of(definition.ContainingType)),
                    findings: Followed.Nothing.Findings,
                    outs: Followed.Nothing.Outs,
                    sets: Followed.Nothing.Sets,
                    through: Followed.Nothing.Through,
                    alwaysGives: Followed.Nothing.AlwaysGives);
            case false when this.depth >= MaxInlineDepth || !this.region.TryEnter():
                return null;
        }

        Dictionary<ISymbol, LoopState> members = new(SymbolEqualityComparer.Default);

        LoopStateEvaluator inner =
            this.Inner(
                call: call,
                bindings: bindings,
                definer: null,
                members: members,
                function: null,
                eachRun: null,
                self: null);

        // The constructor that a constructor calls first builds the same object.
        inner.builtType = chained ? this.builtType : definition.ContainingType;
        inner.thisInTheStore = chained ? this.thisInTheStore : this.BuiltBeforeTheConstructor(call);

        List<Finding> found = [];
        SinkWalker walker = inner.ReadBy(new SinkWalker(region: this.region, evaluator: inner, sink: found.Add));
        SemanticModel model = this.region.ModelFor(type);

        if (declaration is TypeDeclarationSyntax)
        {
            foreach (IParameterSymbol parameter in definition.Parameters)
            {
                if (!inner.bound.TryGetValue(key: parameter, value: out LoopState? given)
                    || given is { IsNone: true, Objects.IsEmpty: true })
                {
                    continue;
                }

                // A method of the object can read a parameter of a primary constructor.
                members[parameter] = given;

                // A positional parameter of a record is also the property of that name. That
                // property has no declaration of its own.
                if (declaration is RecordDeclarationSyntax
                    && definition.ContainingType.GetMembers(parameter.Name)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault(static property =>
                            property.DeclaringSyntaxReferences.Any(static reference =>
                                reference.GetSyntax() is ParameterSyntax)) is { } positional)
                {
                    members[positional] = given;
                }
            }
        }

        // The constructor that this one calls first. The walk gives its members to this object.
        SyntaxNode? chain =
            declaration switch
            {
                ConstructorDeclarationSyntax written => written.Initializer,
                TypeDeclarationSyntax =>
                    type.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault(),
                _ => null
            };

        walker.Visit(chain);

        // With no initializer, the constructor calls the constructor of the base type with no
        // arguments first.
        if (chain == null
            && ImplicitChainOf(declaration: declaration, type: type, definition: definition) is (var at, var first)
            && inner.FollowChainTo(chain: at, constructor: first, arguments: default) is { } chainedFirst)
        {
            found.AddRange(chainedFirst.Findings);
        }

        // The initializers of the members run in each constructor, but not in one that calls a
        // second constructor of the type first. The compiler writes no initializer into the copy
        // constructor of a record, which a with expression calls.
        if (chain is not ConstructorInitializerSyntax { RawKind: (int)SyntaxKind.ThisConstructorInitializer }
            && !IsCopyConstructor(definition))
        {
            foreach (MemberDeclarationSyntax member in type.Members)
            {
                switch (member)
                {
                    case PropertyDeclarationSyntax { Initializer: { } init } property
                        when !IsStatic(property.Modifiers):
                        Set(member: model.GetDeclaredSymbol(property), value: init.Value);
                        break;
                    case FieldDeclarationSyntax field when !IsStatic(field.Modifiers):
                        foreach (VariableDeclaratorSyntax declarator in field.Declaration.Variables)
                        {
                            if (declarator.Initializer != null)
                            {
                                Set(member: model.GetDeclaredSymbol(declarator), value: declarator.Initializer.Value);
                            }
                        }

                        break;
                }
            }
        }

        // The body. The walk records each member that the body sets.
        if (declaration is ConstructorDeclarationSyntax explicitConstructor)
        {
            walker.Visit(explicitConstructor.Body ?? (SyntaxNode?)explicitConstructor.ExpressionBody?.Expression);
        }

        found.AddRange(inner.Frame.TakeUnread());

        IReadOnlyList<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving = walker.RunWaitingApart();

        return this.Leave(
            call: call,
            name: definition.ContainingType.Name,
            bindings: bindings,
            inner: inner,
            lexical: false,
            receiver: null,
            target: null,
            result: LoopState.None.WithMembers(
                Construction.Of(
                    members: members.ToImmutableDictionary(SymbolEqualityComparer.Default),
                    type: definition.ContainingType)),
            found: found,
            leaving: leaving,
            thrown: walker.Thrown);

        void Set(ISymbol? member, ExpressionSyntax value)
        {
            if (member == null)
            {
                return;
            }

            walker.Visit(value);
            inner.SetMember(member: member, state: inner.Eval(value), replaces: true, from: value);
        }
    }

    // The constructor of the base type that a constructor with no initializer calls, and a node for
    // that call. The node is the parameter list of the constructor, or the base type in the list
    // of the type when the compiler writes the constructor. Null for a base type with no code, and
    // for a base type with no constructor that has no parameters.
    private static (SyntaxNode At, IMethodSymbol Constructor)? ImplicitChainOf(
        SyntaxNode? declaration,
        BaseTypeDeclarationSyntax type,
        IMethodSymbol definition)
    {
        if (IsCopyConstructor(definition)
            || definition.ContainingType is not
            {
                TypeKind: TypeKind.Class,
                BaseType: { DeclaringSyntaxReferences.IsEmpty: false } baseType
            }
            || baseType.InstanceConstructors.FirstOrDefault(static constructor => constructor.Parameters.IsEmpty)
                is not { } first)
        {
            return null;
        }

        SyntaxNode? at =
            declaration is ConstructorDeclarationSyntax written
                ? written.ParameterList
                : type.BaseList?.Types.FirstOrDefault();

        return at == null ? null : (at, first);
    }

    /// <summary>
    ///     The read of a property whose getter has a body. The getter is a member
    ///     of the object that owns the property, and reads the members of that
    ///     object.
    /// </summary>
    private Followed? FollowGetter(SyntaxNode access)
    {
        if (this.region.SymbolOf(access) is not IPropertySymbol written || this.region.Symbols.IsSodaFlow(written))
        {
            return null;
        }

        ExpressionSyntax? owner =
            access switch
            {
                MemberAccessExpressionSyntax member => member.Expression,
                MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
                _ => null
            };

        if (SodaFlowSymbols.IsLazyValue(written))
        {
            return owner == null
                ? null
                : this.FollowLazy(access: access, written: owner, lazy: this.Eval(owner), value: written.Type);
        }

        // A property that the code reads through an interface is the property of the object. A
        // value that is one of two objects has the property of each.
        IPropertySymbol[] onThis =
            owner is null or ThisExpressionSyntax
                ? [.. this.ImplementationsOnThis(written).OfType<IPropertySymbol>()]
                : [];

        return (onThis.Length > 0
                ? onThis
                : PropertiesOf(
                    owner: this.builtType != null && owner is null or ThisExpressionSyntax
                        ? Construction.Of(this.builtType)
                        : this.Self(owner).Members,
                    written: written))
            .Aggregate(
                seed: (Followed?)null,
                func: (all, property) =>
                {
                    if (GetterOf(property.OriginalDefinition) is not { } body)
                    {
                        return all;
                    }

                    IParameterSymbol? extended = owner == null ? null : this.ExtensionParameterOf(property);

                    (Dictionary<ISymbol, LoopState> members, LoopState? receiver) =
                        this.MembersFor(isStatic: property.IsStatic || extended != null, owner: owner);

                    return Both(
                        first: all,
                        second: this.Enter(
                            call: access,
                            name: property.Name,
                            body: body,
                            bindings: extended != null && owner != null
                                ? [new Binding(parameter: extended, argument: owner, state: this.Eval(owner))]
                                : [],
                            definer: null,
                            members: members,
                            receiver: receiver,
                            target: null,
                            returnsAnObject: this.DoesNotSayItsClass(property.Type)));
                });
    }

    // The parameter of the extension block of C# 14 that declares a member. The member reads its
    // receiver through that parameter. Null for each other member, and for a static member.
    private IParameterSymbol? ExtensionParameterOf(ISymbol definition) =>
        definition is { IsStatic: false, ContainingType: { } block }
        && block.TypeKind == SodaFlowSymbols.ExtensionKind
        && block.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is TypeDeclarationSyntax { ParameterList.Parameters.Count: 1 } declaration
            ? this.region.ModelFor(declaration).GetDeclaredSymbol(declaration.ParameterList.Parameters[0])
            : null;

    private static IEnumerable<IPropertySymbol> PropertiesOf(Construction? owner, IPropertySymbol written)
    {
        IPropertySymbol[] actual = [.. owner?.Resolve(written).OfType<IPropertySymbol>() ?? []];

        return actual.Length == 0 ? [written] : actual;
    }

    private Followed? FollowGetterOn(ExpressionSyntax access, IPropertySymbol written, LoopState owner)
    {
        if (SodaFlowSymbols.IsLazyValue(written))
        {
            return this.FollowLazy(access: access, written: access, lazy: owner, value: written.Type);
        }

        // A property that the pattern reads through an interface is the property of the object.
        return PropertiesOf(owner: owner.Members, written: written)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, property) =>
                    property.IsStatic || GetterOf(property.OriginalDefinition) is not { } body
                        ? all
                        : Both(
                            first: all,
                            second: this.Enter(
                                call: access,
                                name: property.Name,
                                body: body,
                                bindings: [],
                                definer: null,
                                members: TaggedMembers(owner),
                                receiver: owner,
                                target: null)));
    }

    /// <summary>
    ///     The read of <c>Value</c> of a <c>Lazy</c>, which runs the functions
    ///     that the <c>Lazy</c> has. A call of <c>RunSynchronously</c> runs the
    ///     functions of a task, as this read does. The result is null when it has
    ///     no function that the evaluator can read.
    /// </summary>
    /// <remarks>
    ///     A <c>Lazy</c> from a <c>Map</c> has two functions, and the second one
    ///     gets the value of the first one as its parameter. Thus, a second walk
    ///     gives each parameter the result of the first walk also.
    /// </remarks>
    /// <param name="access">The read of the property.</param>
    /// <param name="written">The expression that names the <c>Lazy</c>, for a report.</param>
    /// <param name="lazy">The state of the <c>Lazy</c>.</param>
    /// <param name="value">The type of the value.</param>
    private Followed? FollowLazy(SyntaxNode access, ExpressionSyntax written, LoopState lazy, ITypeSymbol value)
    {
        // A Lazy of a delegate can hold a function as its value. The read gives that function,
        // and does not run it. A function that makes such a value returns a delegate.
        ImmutableArray<FunctionValue> makers =
            value.TypeKind == TypeKind.Delegate
                ? lazy.Functions.Where(this.ReturnsDelegate).ToImmutableArray()
                : lazy.Functions;

        if (makers.IsEmpty)
        {
            return null;
        }

        Followed? first = Run(lazy);

        Followed? followed =
            first == null || makers.Length == 1 || first.State.IsNone ? first : Run(lazy.Join(first.State));

        if (followed == null || makers.Length == lazy.Functions.Length)
        {
            return followed;
        }

        return new Followed(
            state: lazy.Functions
                .Where(function => !makers.Contains(function))
                .Aggregate(
                    seed: followed.State,
                    func: static (state, function) => state.Join(LoopState.OfFunction(function))),
            findings: followed.Findings,
            outs: followed.Outs,
            sets: followed.Sets,
            through: followed.Through,
            alwaysGives: followed.AlwaysGives,
            thrown: followed.Thrown);

        Followed? Run(LoopState given) =>
            makers.Aggregate(
                seed: (Followed?)null,
                func: (all, function) =>
                    Both(
                        first: all,
                        second: this.FollowFunction(
                            call: access,
                            name: NameOf(written),
                            function: function,
                            target: lazy,
                            arguments: default,
                            each: (written, given, ManyTimes: true))));
    }

    private bool ReturnsDelegate(FunctionValue function) =>
        (function.Declaration is AnonymousFunctionExpressionSyntax
            ? this.region.SymbolOf(function.Declaration)
            : this.region.ModelFor(function.Declaration).GetDeclaredSymbol(function.Declaration))
        is IMethodSymbol { ReturnType.TypeKind: TypeKind.Delegate };

    /// <summary>The body of a method or a local function of this compilation.</summary>
    private static SyntaxNode? BodyOf(ISymbol definition) =>
        definition.DeclaringSyntaxReferences
            .Select(static reference =>
                reference.GetSyntax() switch
                {
                    MethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
                    LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                    OperatorDeclarationSyntax operation =>
                        operation.Body ?? (SyntaxNode?)operation.ExpressionBody?.Expression,
                    ConversionOperatorDeclarationSyntax conversion =>
                        conversion.Body ?? (SyntaxNode?)conversion.ExpressionBody?.Expression,
                    AccessorDeclarationSyntax accessor =>
                        accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression,
                    // The getter of a property or an indexer that is only an expression.
                    ArrowExpressionClauseSyntax arrow => arrow.Expression,
                    _ => null
                })
            .FirstOrDefault(static body => body != null);

    /// <summary>The body of the getter of a property, when the getter has one.</summary>
    private static SyntaxNode? GetterOf(ISymbol definition) =>
        definition.DeclaringSyntaxReferences
            .Select(static reference =>
                reference.GetSyntax() switch
                {
                    PropertyDeclarationSyntax { ExpressionBody: { } arrow } => arrow.Expression,
                    PropertyDeclarationSyntax { AccessorList: { } accessors } =>
                        accessors.Accessors
                            .Where(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                            .Select(static accessor =>
                                accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression)
                            .FirstOrDefault(static body => body != null),
                    _ => null
                })
            .FirstOrDefault(static body => body != null);

    private static bool IsStatic(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier =>
            modifier.IsKind(SyntaxKind.StaticKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword));

    /// <summary>
    ///     The delegate that an invocation calls: <c>f</c> in <c>f(x)</c>, in
    ///     <c>f.Invoke(x)</c>, and in <c>f?.Invoke(x)</c>.
    /// </summary>
    private ExpressionSyntax DelegateOf(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access
                when this.region.SymbolOf(access) is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } =>
                access.Expression,
            MemberBindingExpressionSyntax binding when ConditionalReceiver(binding) is { } receiver => receiver,
            _ => invocation.Expression
        };

    private static string NameOf(ExpressionSyntax target) =>
        target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => target.ToString()
        };

    /// <summary>
    ///     The members of a bindable action. The firings are the sink that the
    ///     action gets. The action listens to its enablement cell, and that cell
    ///     links nothing into the sink.
    /// </summary>
    private Construction ActionParts(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        Construction parts = Construction.Empty;

        for (int i = 0; i < arguments.Count; i++)
        {
            string? member =
                ParameterFor(method: method, argument: arguments[i], position: i)?.Name switch
                {
                    "firingsStreamSink" => "FiringsStream",
                    "isEnabledCell" => "IsEnabledCell",
                    _ => null
                };

            if (member != null
                && method.ReturnType.AllInterfaces
                        .Add(item: (INamedTypeSymbol)method.ReturnType)
                        .SelectMany(type => type.GetMembers(member))
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault()
                    is { } property)
            {
                parts = parts.With(member: property, state: this.Eval(arguments[i].Expression));
            }
        }

        return parts;
    }

    /// <summary>
    ///     A method with no rule here gives its result the state of its inputs,
    ///     where the result type can hold one. This is the conservative default.
    /// </summary>
    private static LoopState DefaultLibraryResult(SodaFlowSymbols symbols, IMethodSymbol method, LoopState inputs)
    {
        ITypeSymbol returnType = method.ReturnType;

        if (symbols.IsStreamType(returnType))
        {
            return new LoopState(
                pending: false,
                linked: inputs.Linked,
                contentPending: inputs.ContentPending,
                contentLinked: inputs.ContentLinked);
        }

        return symbols.IsCarrierType(returnType) || SodaFlowSymbols.IsOfAPackageWithNoGraph(returnType)
            ? inputs
            : LoopState.None;
    }

    /// <summary>
    ///     What a body returns: its one expression, or the combined state of its
    ///     return statements. The evaluator declares the locals at the first level
    ///     of a block in order. This is for a function that runs after the close,
    ///     which the walk does not read.
    /// </summary>
    private LoopState EvalBody(ExpressionSyntax? expressionBody, BlockSyntax? block)
    {
        if (expressionBody != null)
        {
            return this.Eval(expressionBody);
        }

        if (block == null)
        {
            return LoopState.None;
        }

        SemanticModel model = this.region.ModelFor(block);

        // The locals of the block, in order, so that a return of one of them has a state. A local
        // with no initializer gets no state.
        foreach (VariableDeclaratorSyntax declarator in block.Statements
                     .OfType<LocalDeclarationStatementSyntax>()
                     .SelectMany(static declaration => declaration.Declaration.Variables))
        {
            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.Declare(
                    local: local,
                    state: this.Eval(declarator.Initializer?.Value),
                    from: declarator.Initializer?.Value);
            }
        }

        return this.JoinAll(ReturnExpressions(block));
    }

    /// <summary>
    ///     True when a sequence that the code makes at the call reads a pending
    ///     value when something enumerates it. A quiet evaluator reads the sequence.
    ///     Thus, the queries of this code stay for the enumeration that the walk
    ///     reads.
    /// </summary>
    private bool SequenceReadsPending(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        List<Finding> found = [];
        LoopStateEvaluator quiet = this.Quiet();
        SinkWalker? walker = null;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax written = arguments[i].Expression;

            if (SinkWalker.IsMadeAtTheCall(written)
                && SodaFlowSymbols.IsSequence(ParameterFor(method: method, argument: arguments[i], position: i)?.Type))
            {
                walker ??= quiet.ReadBy(new SinkWalker(region: this.region, evaluator: quiet, sink: found.Add));
                walker.RunQueriesMadeHere(written);
            }
        }

        return found.Any(static finding => !finding.Escape);
    }

    /// <summary>
    ///     The functions of the value that a constant or a sink gets. An operator
    ///     argument with functions gives no state, thus the evaluator reads the
    ///     functions here.
    /// </summary>
    private LoopState KeptFunction(SeparatedSyntaxList<ArgumentSyntax> arguments, ExpressionSyntax? receiver)
    {
        ExpressionSyntax? value =
            arguments.Select(static argument => argument.Expression).FirstOrDefault(written => written != receiver);

        // Only a lambda or a name can be a function of this compilation.
        if (value is not (AnonymousFunctionExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return LoopState.None;
        }

        LoopState state = this.Eval(value);

        return state.Functions.IsEmpty ? LoopState.None : state;
    }

    /// <summary>
    ///     False when no function in the arguments can use a value with a state.
    ///     This is a check of the syntax only. For most functions, the evaluator
    ///     then does not read the function. A function gets such a value through
    ///     its parameters, or through a name that has a state. It can also get one
    ///     through a call with no owner, which can be a local function or a member
    ///     of this object.
    /// </summary>
    private bool CanUseState(InvocationExpressionSyntax invocation, LoopState given)
    {
        HashSet<string>? names = null;

        foreach (ExpressionSyntax written in invocation.ArgumentList.Arguments
                     .Select(static argument => argument.Expression))
        {
            if (written is not AnonymousFunctionExpressionSyntax lambda)
            {
                // A name can be a function of this compilation. A comparer with a state can
                // have a function, or can be an object whose methods the operator calls.
                if (this.region.SymbolOf(written) is IMethodSymbol
                    || (this.region.TypeOf(written) is { } type
                        && (type.TypeKind == TypeKind.Delegate
                            || (SodaFlowSymbols.IsComparer(type) && !this.Eval(written).IsNone)
                            || this.IsCellOfFunctions(type))))
                {
                    return true;
                }

                continue;
            }

            if (!this.CanRead(lambda.Body))
            {
                continue;
            }

            if (!given.IsNone || this.region.HasFields)
            {
                return true;
            }

            names ??= this.NamesWithState();

            if (lambda.Body.DescendantNodesAndSelf()
                .Any(node =>
                    node switch
                    {
                        ThisExpressionSyntax => this.thisMembers.Count > 0,
                        InvocationExpressionSyntax { Expression: IdentifierNameSyntax or GenericNameSyntax } => true,
                        IdentifierNameSyntax name => names.Contains(name.Identifier.ValueText),
                        _ => false
                    }))
            {
                return true;
            }
        }

        return false;
    }

    // A cell of functions, as Apply and a filter by a cell of predicates get.
    private bool IsCellOfFunctions(ITypeSymbol type) =>
        // ReSharper disable once MergeIntoPattern - A list pattern needs System.Index, and netstandard2.0 has none.
        type is INamedTypeSymbol { TypeArguments.Length: 1 } cell
        && cell.TypeArguments[0].TypeKind == TypeKind.Delegate
        && this.region.Symbols.IsCellOrBehaviorType(cell);

    private HashSet<string> NamesWithState()
    {
        HashSet<string> names = [this.region.PlaceholderName];

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.locals.Where(static pair => !pair.Value.IsNone))
        {
            names.Add(pair.Key.Name);
        }

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.bound.Where(static pair => !pair.Value.IsNone))
        {
            names.Add(pair.Key.Name);
        }

        foreach (ISymbol member in this.thisMembers.Keys)
        {
            names.Add(member.Name);
        }

        return names;
    }

    // True when a lambda can read a value. It has a call, or a member named Value or CanExecute. It
    // can also have a node that runs code of this compilation with no call syntax. That is a getter
    // with a body, a creation of a type that runs code, an indexer, or an operator. See DeclaredCode.
    // An indexer of the platform can call a member of a key, as a dictionary calls GetHashCode.
    private bool CanRead(SyntaxNode body)
    {
        SodaFlowSymbols symbols = this.region.Symbols;

        return body.DescendantNodesAndSelf()
            .Any(node =>
                node is InvocationExpressionSyntax
                    or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" or "CanExecute" }
                    or MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" or "CanExecute" }
                || (node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax
                    && (symbols.DeclaresIndexers || symbols.DeclaresKeyMembers))
                || symbols.DeclaredCode.CanRunCode(node));
    }

    /// <summary>
    ///     The state of an argument of a library operator. A function that the
    ///     operator keeps for a firing after the close gives the operator no state.
    /// </summary>
    private LoopState EvalArgument(ExpressionSyntax argument, bool runsNow)
    {
        if (argument is AnonymousFunctionExpressionSyntax lambda)
        {
            return runsNow
                ? this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray<LoopState>.Empty)
                : LoopState.None;
        }

        LoopState state = this.Eval(argument);

        // The functions that a name can be are not an input of the operator.
        return state.Functions.IsEmpty ? state : LoopState.None;
    }

    private LoopState EvalLambdaResult(
        AnonymousFunctionExpressionSyntax lambda,
        ImmutableArray<LoopState> boundParameters)
    {
        SemanticModel model = this.region.ModelFor(lambda);
        ImmutableDictionary<ISymbol, LoopState>.Builder parameters = this.bound.ToBuilder();
        IReadOnlyList<ParameterSyntax> written = ParametersOf(lambda);

        for (int i = 0; i < written.Count && i < boundParameters.Length; i++)
        {
            if (model.GetDeclaredSymbol(written[i]) is { } symbol)
            {
                parameters[symbol] = boundParameters[i];
            }
        }

        // The lambda is part of this code: it reads the same locals and the same members.
        LoopStateEvaluator inner =
            boundParameters.IsEmpty
                ? this
                : new LoopStateEvaluator(
                    region: this.region,
                    bound: parameters.ToImmutable(),
                    noValues: this.noValues,
                    depth: this.depth,
                    frame: this.Frame,
                    locals: this.locals,
                    assigned: this.assigned,
                    thisMembers: this.thisMembers,
                    setOnThis: this.setOnThis,
                    quiet: this.quiet,
                    function: this.function,
                    call: this.call) { walker = this.walker };

        return inner.EvalBody(expressionBody: lambda.ExpressionBody, block: lambda.Block);
    }

    // Arrays, and not a collection expression. A collection expression here takes the natural type
    // of the other arms, SeparatedSyntaxList, and that conversion gives an empty list.
    public static IReadOnlyList<ParameterSyntax> ParametersOf(AnonymousFunctionExpressionSyntax lambda) =>
        lambda switch
        {
            SimpleLambdaExpressionSyntax s => new[] { s.Parameter },
            ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.ToArray(),
            AnonymousMethodExpressionSyntax { ParameterList: { } list } => list.Parameters.ToArray(),
            _ => Array.Empty<ParameterSyntax>()
        };

    /// <summary>
    ///     The expressions that a block returns, without those of a nested
    ///     function. An iterator returns a sequence of the values that it yields.
    ///     A sequence has the state of its elements, thus those values are here
    ///     also.
    /// </summary>
    /// <summary>Each expression whose value a lambda can return.</summary>
    public static IEnumerable<ExpressionSyntax> ResultsOf(AnonymousFunctionExpressionSyntax lambda) =>
        lambda.Body is ExpressionSyntax result ? [result] : ReturnExpressions(lambda.Body);

    public static IEnumerable<ExpressionSyntax> ReturnExpressions(SyntaxNode body) =>
        body.DescendantNodes(
                descendIntoChildren: static n =>
                    n is not (AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax))
            .Select(static statement =>
                statement switch
                {
                    ReturnStatementSyntax returned => returned.Expression,
                    YieldStatementSyntax yielded => yielded.Expression,
                    _ => null
                })
            .OfType<ExpressionSyntax>();

    // The state of a value that is one of the given expressions. The state of two values together
    // cannot tell a null from a value with no state that the walk did not see made. The
    // expressions can. One expression is an object of the store, and a second one can be an object
    // with no state. The value can then be an object that the store does not know.
    private LoopState OneOf(IReadOnlyList<ExpressionSyntax> values)
    {
        LoopState all = this.JoinAll(values);

        if (all.Objects.IsEmpty
            || !values.Any(value =>
                this.Eval(value).Objects.IsEmpty
                && !this.IsNoValue(value)
                && this.region.TypeOf(value) is { } type
                && this.CanBeAnObject(type)))
        {
            return all;
        }

        // The members are for the object that the store does not know. See Kept.
        return all.WithMembers(
                values.Aggregate(seed: LoopState.None, func: (kept, value) => kept.Join(this.Kept(value))).Members)
            .OrAnUnknownObject;
    }

    private LoopState JoinAll(IEnumerable<ExpressionSyntax?> expressions) =>
        expressions.Aggregate(seed: LoopState.None, func: (state, e) => state.Join(this.Eval(e)));

    private LoopState TypeState(ExpressionSyntax expression) =>
        this.region.Symbols.StateOfType(this.region.TypeOf(expression));

    private static bool LinkedStreamInput(
        SodaFlowSymbols symbols,
        LoopState receiverState,
        ITypeSymbol? receiverType,
        IReadOnlyList<LoopState> argumentStates,
        IReadOnlyList<ITypeSymbol?> argumentTypes)
    {
        bool linked =
            receiverState.Linked && (receiverType == null || !symbols.IsCellOrBehaviorType(receiverType));

        for (int i = 0; i < argumentStates.Count; i++)
        {
            linked |= argumentStates[i].Linked && !symbols.IsCellOrBehaviorType(argumentTypes[i]);
        }

        return linked;
    }

    /// <summary>
    ///     An <c>AsCell()</c> or <c>AsBehavior()</c> on the argument, or on a
    ///     value it is derived from, is the assertion that nothing reads it before the
    ///     close. Parentheses, a cast, and the operator that says not null do not
    ///     hide it. A local has its only value, also in a lambda: see
    ///     <see cref="OnlyValueOf" />.
    /// </summary>
    public bool IsAsserted(ExpressionSyntax argument)
    {
        ExpressionSyntax current = Unwrapped(argument);

        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }:
                    if (access.Name.Identifier.ValueText is "AsCell" or "AsBehavior")
                    {
                        return true;
                    }

                    current = Unwrapped(access.Expression);

                    break;
                case IdentifierNameSyntax name
                    when this.region.SymbolOf(name) is ILocalSymbol local && this.OnlyValueOf(local) is { } only:
                    current = Unwrapped(only);

                    break;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    ///     The name of the operator, without the Impl or Internal suffix of the
    ///     library's own internal entry points.
    /// </summary>
    public static string OperatorName(IMethodSymbol method)
    {
        string name = method.Name;

        string? suffix =
            Suffixes.FirstOrDefault(candidate =>
                name.Length > candidate.Length
                && name.EndsWith(value: candidate, comparisonType: StringComparison.Ordinal));

        return suffix == null ? name : name.Substring(startIndex: 0, length: name.Length - suffix.Length);
    }

    /// <summary>
    ///     The expression that receives a call. The call can be
    ///     <c>receiver.M()</c> or <c>receiver?.M()</c>. For an extension method of
    ///     the library, it can also be <c>Extensions.M(receiver)</c>.
    /// </summary>
    public static ExpressionSyntax? ReceiverOf(
        SodaFlowSymbols symbols,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.ReducedFrom != null || !method.IsStatic)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax access => access.Expression,
                MemberBindingExpressionSyntax => ConditionalReceiver(invocation),
                _ => null
            };
        }

        // A static call. Only an operator of the library has a rule that asks for its receiver.
        if (!method.IsExtensionMethod || !symbols.IsSodaFlow(method))
        {
            return null;
        }

        return invocation.ArgumentList.Arguments
            .Where((argument, position) =>
                SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position) is { Ordinal: 0 })
            .Select(static argument => argument.Expression)
            .FirstOrDefault();
    }

    /// <summary>
    ///     The expression before the <c>?.</c> of a conditional access, for a
    ///     member or a call that comes after it.
    /// </summary>
    public static ExpressionSyntax? ConditionalReceiver(SyntaxNode afterTheMark) =>
        afterTheMark.Ancestors()
            .OfType<ConditionalAccessExpressionSyntax>()
            .FirstOrDefault(conditional => conditional.WhenNotNull.Span.Contains(afterTheMark.Span))
            ?.Expression;

    // Two implied calls are the same when the node and the member are the same.
    private sealed class ImpliedCalls : IEqualityComparer<(SyntaxNode Node, ISymbol Member)>
    {
        public static readonly ImpliedCalls Instance = new();

        public bool Equals((SyntaxNode Node, ISymbol Member) x, (SyntaxNode Node, ISymbol Member) y) =>
            ReferenceEquals(objA: x.Node, objB: y.Node)
            && SymbolEqualityComparer.Default.Equals(x: x.Member, y: y.Member);

        public int GetHashCode((SyntaxNode Node, ISymbol Member) call) =>
            (call.Node.GetHashCode() * 397) ^ SymbolEqualityComparer.Default.GetHashCode(call.Member);
    }

    /// <summary>One argument of a call, with the parameter of the callee that gets it.</summary>
    // One value that a call gives to code of the platform. The parameter is the type that the
    // definition of the callee has for it. It is null for the receiver of a method of the callee.
    private readonly struct Operand(in ExpressionSyntax written, in ITypeSymbol? parameter, in bool isReceiver)
    {
        public ExpressionSyntax Written { get; } = written;

        public ITypeSymbol? Parameter { get; } = parameter;

        public bool IsReceiver { get; } = isReceiver;
    }

    private readonly struct Binding(in IParameterSymbol parameter, in ExpressionSyntax argument, in LoopState state)
    {
        public IParameterSymbol Parameter { get; } = parameter;

        public ExpressionSyntax Argument { get; } = argument;

        public LoopState State { get; } = state;
    }

    /// <summary>
    ///     The call that an evaluator reads its code for. The code is a callee, or
    ///     a lambda or a local function that the walk reads for a call of it.
    /// </summary>
    /// <param name="runsOneTime">The value of <see cref="RunsOneTime" />.</param>
    /// <param name="turn">The value of <see cref="Turn" />.</param>
    /// <param name="alwaysRuns">The value of <see cref="AlwaysRuns" />.</param>
    /// <param name="alwaysTurn">The value of <see cref="AlwaysTurn" />.</param>
    /// <param name="frameOfTheAlwaysTurn">The value of <see cref="FrameOfTheAlwaysTurn" />.</param>
    /// <param name="code">The value of <see cref="Code" />.</param>
    /// <param name="caller">The value of <see cref="Caller" />.</param>
    /// <param name="site">The value of <see cref="Site" />.</param>
    private sealed class CallOfThisCode(
        in bool runsOneTime,
        in SyntaxNode? turn,
        in bool runsAFunctionOfThisRun,
        in ImmutableArray<SyntaxNode> loopsAroundTheTurn,
        in bool alwaysRuns,
        in SyntaxNode? alwaysTurn,
        in LoopFrame? frameOfTheAlwaysTurn,
        in object code,
        in MadeObject? self,
        in bool hasItsOwnMembers,
        in ImmutableDictionary<ISymbol, ITypeSymbol> typeArguments,
        in Locations locations,
        in LoopStateEvaluator caller,
        in SyntaxNode site)
    {
        /// <summary>True when the call runs one time at most.</summary>
        public bool RunsOneTime { get; } = runsOneTime;

        /// <summary>
        ///     The body of the loop whose each turn runs the call one time at
        ///     most. Null when no loop does.
        /// </summary>
        public SyntaxNode? Turn { get; } = turn;

        /// <summary>
        ///     True when the call runs a lambda or a local function that this run
        ///     of the code around the call made. The function then reads the
        ///     locals of this run, and not those of a turn before.
        /// </summary>
        public bool RunsAFunctionOfThisRun { get; } = runsAFunctionOfThisRun;

        /// <summary>
        ///     The bodies of the loops around the loop of <see cref="Turn" />,
        ///     the nearest first.
        /// </summary>
        public ImmutableArray<SyntaxNode> LoopsAroundTheTurn { get; } = loopsAroundTheTurn;

        /// <summary>
        ///     True when each run of the block runs the call. The call is then
        ///     in no branch and in no loop, in its function and in each function
        ///     that calls that function.
        /// </summary>
        public bool AlwaysRuns { get; } = alwaysRuns;

        /// <summary>
        ///     The body of the loop whose each turn always runs the call. Null
        ///     when no loop does.
        /// </summary>
        public SyntaxNode? AlwaysTurn { get; } = alwaysTurn;

        /// <summary>
        ///     The frame of the code that has the loop of <see cref="AlwaysTurn" />,
        ///     when that is not null. That code is the caller, or a caller of the
        ///     caller.
        /// </summary>
        public LoopFrame? FrameOfTheAlwaysTurn { get; } = frameOfTheAlwaysTurn;

        /// <summary>
        ///     An identity for the code of the call, from
        ///     <see cref="ObjectStore.CodeOf" />.
        /// </summary>
        public object Code { get; } = code;

        /// <summary>
        ///     The object that the code of the call is in, when the owner in the
        ///     call is a local that is one object. Null for each other call.
        /// </summary>
        public MadeObject? Self { get; } = self;

        /// <summary>
        ///     True when the code of the call has a copy of the members of its
        ///     object, or the members of a new object. False when it has the
        ///     members of the object that the code of the caller is in.
        /// </summary>
        public bool HasItsOwnMembers { get; } = hasItsOwnMembers;

        /// <summary>
        ///     The type that the call gives each type parameter of its method.
        ///     The code of the call can make an object of such a type.
        /// </summary>
        public ImmutableDictionary<ISymbol, ITypeSymbol> TypeArguments { get; } = typeArguments;

        /// <summary>
        ///     For a parameter with ref of the code of the call: the expression of
        ///     its location. The second part is the evaluator of the code that has
        ///     that expression. That code is the caller, or a caller of the caller.
        /// </summary>
        public Locations Locations { get; } = locations;

        /// <summary>The evaluator of the code that has the call.</summary>
        public LoopStateEvaluator Caller { get; } = caller;

        /// <summary>The call.</summary>
        public SyntaxNode Site { get; } = site;
    }
}
