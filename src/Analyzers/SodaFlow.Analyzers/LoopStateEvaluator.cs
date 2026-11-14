using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SodaFlow.Analyzers;

/// <summary>
///     Gives each expression in a loop region its <see cref="LoopState" />.
///     The rules are a model of what each library operator does with the current
///     value and the links of its inputs. A call to code of this compilation gets
///     its state from the body of that code. The evaluator walks that body as
///     the region walks the block, and gives the caller what the walk found.
/// </summary>
internal sealed class LoopStateEvaluator
{
    /// <summary>
    ///     How many calls deep the evaluator follows a helper or a construction.
    ///     Four is sufficient for a sort two helpers down. The two helpers are two
    ///     calls, and the member of an element and its own helper are two more.
    ///     The limit is also what ends a recursive chain.
    /// </summary>
    private const int MaxInlineDepth = 4;

    private static readonly ImmutableArray<string> Suffixes = ImmutableArray.Create(item1: "Impl", item2: "Internal");

    private static readonly ImmutableDictionary<ISymbol, LoopState> NoSymbols =
        ImmutableDictionary<ISymbol, LoopState>.Empty.WithComparers(SymbolEqualityComparer.Default);

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

    // The lambda or the local function that this evaluator reads for a call of it. Null for each
    // other code.
    private readonly SyntaxNode? function;

    private readonly Dictionary<ExpressionSyntax, LoopState> functionResults = [];

    // The queries that this code makes.
    private readonly Dictionary<SyntaxNode, QueryValue> queries = [];

    // True for an evaluator that reads code that does not run now, to learn what that code reads.
    // It has copies of the locals and the members, and it does not set a field of the region.
    private readonly bool quiet;
    private readonly LoopRegion region;
    private readonly Dictionary<ISymbol, LoopState> thisMembers;

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
            quiet: false,
            function: null)
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
        bool quiet,
        SyntaxNode? function)
    {
        this.function = function;
        this.region = region;
        this.bound = bound;
        this.noValues = noValues;
        this.depth = depth;
        this.Frame = frame;
        this.locals = locals;
        this.assigned = assigned;
        this.thisMembers = thisMembers;
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
                quiet: true,
                function: this.function);

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
        SodaFlowSymbols.IsEnumerated(definition.ReturnType)
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
    public void Declare(ISymbol local, LoopState state)
    {
        state = this.InALocal(local: local, state: state);

        this.locals[local] =
            this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing.Join(state) : state;
    }

    /// <summary>
    ///     Records an assignment that always runs. The new state replaces the
    ///     state that the local or the parameter had.
    /// </summary>
    public void Assign(ISymbol local, LoopState state)
    {
        this.locals[local] = this.InALocal(local: local, state: state);
        this.assigned.Add(local);
    }

    /// <summary>
    ///     Records an assignment that each turn of a loop always runs, to a local
    ///     that is older than the turn. For the rest of the turn, the local has
    ///     only the new value.
    /// </summary>
    /// <remarks>
    ///     A loop can run no turn, and its last turn can end before the
    ///     assignment. Thus, the caller keeps the state that the local had, and
    ///     gives it back with <see cref="Declare" /> after the loop.
    /// </remarks>
    /// <param name="local">The local.</param>
    /// <param name="state">The state of the new value.</param>
    /// <returns>The state that the local had. None for a local with no state.</returns>
    public LoopState AssignInATurn(ISymbol local, LoopState state)
    {
        LoopState had = this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing : LoopState.None;
        this.locals[local] = this.InALocal(local: local, state: state);

        return had;
    }

    /// <summary>
    ///     True for a local that the given code declares. A local that the body of
    ///     a loop declares is new in each turn.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="code">The body of a loop, or a function.</param>
    public static bool IsDeclaredIn(ISymbol local, SyntaxNode code) =>
        local.DeclaringSyntaxReferences.Any(reference =>
            reference.SyntaxTree == code.SyntaxTree && code.Span.Contains(reference.Span));

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
    ///     it, and the second read finds a read of that value. Each object of the
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
        Forget(found: this.functionResults, code: loop, node: static key => key);
        Forget(found: this.followed, code: loop, node: static key => key);
        Forget(found: this.implied, code: loop, node: static key => key.Node);

        foreach (ISymbol local in this.locals.Keys.Where(local => IsDeclaredIn(local: local, code: turn)).ToArray())
        {
            this.locals.Remove(local);
            this.assigned.Remove(local);
        }
    }

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
                location.Code.RunsInTheTurnOf(made) || IsDeclaredIn(local: location.Local, code: turn));

    // A location that is new in no turn.
    private LoopState InAnOldLocation(LoopState state) =>
        this.InALocation(state: state, location: false, isOfItsTurn: static (_, _, isNew) => isNew);

    // True when the code of this frame runs in one turn of the loop that made the object. The
    // frame is then of a callee of the code that has the loop, and the call did not return. A
    // function that a callee made can run in a turn after that one, with the locals of the callee.
    private bool RunsInTheTurnOf(MadeObject made) => !made.IsFrom(this.Frame) && !this.Frame.HasReturned;

    // A member or an element of an object is new in each turn of the loop that made the object.
    // It is new in each turn of each loop around that loop also. That is true only for a value
    // that is one object. The object must be a new one: the result of a call can be the same
    // object in each turn.
    private LoopState InAnObject(LoopState owner, LoopState state) =>
        this.InALocation(
            state: state,
            location: (Store: this.region.Objects, Turn: TurnOfTheNewOne(owner)),
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
    ///     Records the state of a member of the object that the code is in, at
    ///     its initializer or assignment.
    /// </summary>
    public void SetMember(ISymbol member, LoopState state)
    {
        ISymbol key = member.OriginalDefinition;

        // The object can be older than a turn of a loop of this code. The receiver of a method can
        // be older than a turn of a loop of the caller also. A constructor makes a new object.
        state = this.InALocation(
            state: state,
            location: this,
            isOfItsTurn: static (_, made, code) => code.Frame.IsOfAConstructor && code.RunsInTheTurnOf(made));

        this.thisMembers[key] =
            this.thisMembers.TryGetValue(key: key, value: out LoopState? existing) ? existing.Join(state) : state;
    }

    /// <summary>
    ///     Records a value that the code gives to a member of an object, as in
    ///     <c>row.Share = value</c>. An object in a local keeps the state with its
    ///     members. For each other owner, and for a static member, the region keeps
    ///     the state for the field.
    /// </summary>
    /// <param name="owner">The expression of the object. Null for a static member.</param>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    /// <param name="turn">
    ///     The body of the loop whose each turn always runs the assignment. Null
    ///     when no loop does.
    /// </param>
    public void SetMemberOf(ExpressionSyntax? owner, ISymbol member, LoopState state, bool replaces, SyntaxNode? turn)
    {
        LoopState has = this.Eval(owner);
        state = this.InAnObject(owner: has, state: state);

        LoopState changed = LoopState.None.WithMembers(Construction.Empty.With(member: member, state: state));

        // The object of the store gets the state for the member, for each name of the object.
        if (owner != null)
        {
            this.Change(
                owner: has,
                change: old => old.WithMembers((old.Members ?? Construction.Empty).With(member: member, state: state)),
                replaces: replaces,
                turn: turn);
        }

        // The owner is an element of a collection. The collection then contains an object whose
        // member has the state.
        if (owner != null && this.CollectionOf(owner) != null)
        {
            this.Merge(target: owner, state: changed);
        }

        if (owner is IdentifierNameSyntax
            && this.region.SymbolOf(owner) is { } holder
                and (ILocalSymbol or IParameterSymbol))
        {
            // A collection that got the object of this local contains that object also.
            this.MergeIntoHolders(
                local: holder,
                state: this.AsACopyOfAChangeTo(changed: has, state: changed),
                level: 1);

            // A parameter starts with the state of its argument, which has the type of the object.
            // The callee gives this state back, and the object of the caller keeps its type.
            LoopState known =
                this.locals.TryGetValue(key: holder, value: out LoopState? existing)
                    ? existing
                    : this.bound.TryGetValue(key: holder, value: out LoopState? given)
                        ? given
                        : LoopState.None;

            this.locals[holder] =
                known.WithMembers((known.Members ?? Construction.Empty).With(member: member, state: state));

            return;
        }

        this.SetField(field: member, state: state);
    }

    /// <summary>
    ///     Records that a local has one of the values that a collection contains.
    ///     A change to the object in the local is then a change to what the
    ///     collection contains.
    /// </summary>
    public void HoldsOneOf(ISymbol local, ExpressionSyntax collection) => this.Frame.ElementOf[local] = collection;

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
        }

        if (this.CollectionOf(value) is { } collection)
        {
            this.HoldsOneOf(local: local, collection: collection);
        }
        else if (replaces)
        {
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

            this.Change(
                owner: this.Eval(written),
                change: old => old.HoldingWhatCameWith(put),
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

    private void SetField(ISymbol field, LoopState state)
    {
        if (!this.quiet)
        {
            // The owner of the field is not known, thus it is new in no turn.
            this.region.SetField(field: field, state: this.InAnOldLocation(state));
        }
    }

    /// <summary>
    ///     Adds a state to what a name holds, for a value that a call puts into
    ///     the object that the name is for. <c>list.Add(value)</c> is such a call.
    /// </summary>
    public void Absorb(ExpressionSyntax target, LoopState state)
    {
        // The object contains the value. It is not the value.
        LoopState put = state.AsContainer.WithNoObjects;

        // A value with members that is no object of the store has those members only here. For a
        // collection whose elements go into the object, that is a collection that holds no
        // object. Thus, the object then holds an object that the store does not know.
        bool ofNoObject = state.Members != null && (state.IsContainer ? !state.HoldsAnObject : state.Objects.IsEmpty);

        this.Merge(target: target, state: ofNoObject ? put.HoldingAnUnknownObject : put);
    }

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
        for (; level < MaxOwners; level++)
        {
            // Parentheses, a cast, and the operator that says not null do not hide the name.
            target = Unwrapped(target);
            LoopState owner = this.Eval(target);
            state = this.InAnObject(owner: owner, state: state);
            LoopState added = state;
            this.Change(owner: owner, change: old => old.Join(added), replaces: false, turn: null);

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
                    state: this.AsACopyOfAChangeTo(changed: owner, state: state),
                    level: level + 1);
            }

            if (collection == null)
            {
                return;
            }

            if (this.region.SymbolOf(collection) is ILocalSymbol local)
            {
                this.Frame.Positions[local] = null;
            }

            target = collection;
            state = this.AsACopyOfAChangeTo(changed: owner, state: state);
        }
    }

    // A collection gets a copy of each change to a value that it contains. When the store has
    // each object that the value can be, the store has the change also. For each other value, the
    // copy is the only record of the change. Thus, the collection then holds an object that the
    // store does not know, and an element of it keeps the copy.
    private LoopState AsACopyOfAChangeTo(LoopState changed, LoopState state) =>
        !changed.Objects.IsEmpty && changed.Objects.All(made => this.region.Objects.StateOf(made) != null)
            ? state.AsContainer
            : state.AsContainer.HoldingAnUnknownObject;

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
                this.Declare(local: declared, state: state);
                this.MergeIntoHolders(local: declared, state: state.AsContainer, level: level + 1);
            }
        }
    }

    private void MergeInto(SyntaxNode target, LoopState state, int level)
    {
        switch (this.region.SymbolOf(target))
        {
            case { } local and (ILocalSymbol or IParameterSymbol) when target is IdentifierNameSyntax:
                this.Declare(local: local, state: state);
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

        // The evaluator can go in a cycle only through a reassignment, and none is the correct
        // answer for the first visit.
        this.memo[expression] = LoopState.None;

        // The walk reads a node one time, and the value keeps the state from that read. A second
        // read of the same node does not see a change that came after the first.
        LoopState state =
            this.Refreshed(
                expression: expression,
                state: this.OnlyObjectsOfItsType(
                    expression: expression,
                    state: this.Now(this.Captured(expression: expression, state: this.EvalUncached(expression)))));

        this.memo[expression] = state;

        return state;
    }

    /// <summary>
    ///     The object in a member of one object of the store. Each read of the
    ///     member, through each name of the owner, is that one object. That is
    ///     true also when the walk did not read the constructor of the owner. Null
    ///     for a member whose state has an object of the store, and for a member
    ///     that is not one of this compilation.
    /// </summary>
    /// <remarks>
    ///     A member of the platform, such as <c>Values</c> of a dictionary, keeps
    ///     the rule that a member of a pending value is pending. An evaluator that
    ///     reads code which does not run now makes no object.
    /// </remarks>
    private LoopState? InMember(LoopState owner, ISymbol member, ExpressionSyntax expression)
    {
        if (owner.Objects.Length != 1
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

        MadeObject? made = this.region.Objects.InMember(owner: owner.Objects[0], member: member);

        if (made == null)
        {
            if (this.quiet)
            {
                return null;
            }

            made =
                this.region.Objects.MakeInMember(
                    owner: owner.Objects[0],
                    member: member,
                    type: type,
                    state: inOwner.Join(this.TypeState(expression)));
        }

        return this.region.Objects.StateOf(made)
            ?.WithObjects(ImmutableArray.Create(made))
            .FromIfUnknown(owner.Origin);
    }

    // A location can keep a lambda or a local function from a turn before, and a call then runs
    // that function. A local that the function reads from the code around it is then the local of
    // that turn. Thus, the value of such a local can be an object of the turns before also.
    private LoopState Captured(SyntaxNode expression, LoopState state) =>
        this.function != null
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
    ///     an object that the store does not know for each value that it got with
    ///     members and no object. Thus, an element of such a collection keeps what
    ///     it has, as each value that can be such an object does. The result of a
    ///     call is one of the values that it can be, and keeps what it has also.
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
                all: known
                     && Unwrapped(expression)
                         is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax);

        // The store has the parts of the object of this turn. A value that can be an older object
        // can have the older parts also.
        LoopState parts = this.InAnObject(owner: refreshed, state: refreshed);

        return ReferenceEquals(objA: parts, objB: refreshed) ? refreshed : parts.WithObjects(refreshed.Objects);
    }

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
        LoopState held = state.Holding(state.Objects).WithObjects(ImmutableArray<MadeObject>.Empty);
        LoopState apart = held.IsContainer && held.HasMembersOfNoObject ? held.HoldingAnUnknownObject : held;

        if (this.quiet)
        {
            return apart;
        }

        if (this.RunsOneTime(place))
        {
            return apart.WithObjects(
                ImmutableArray.Create(
                    this.region.Objects.Make(frame: this.Frame, type: type, exact: exact, state: apart)));
        }

        if (TurnOf(place) is not { } turn)
        {
            return apart;
        }

        return apart.WithObjects(
            ImmutableArray.Create(
                this.region.Objects.MakeInATurn(
                    frame: this.Frame,
                    type: type,
                    exact: exact,
                    turn: turn,
                    place: place,
                    state: apart)));
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
    private static SyntaxNode? TurnOf(SyntaxNode place)
    {
        foreach (SyntaxNode ancestor in place.Ancestors())
        {
            if (LoopBodyOf(ancestor) is { } body)
            {
                return body;
            }

            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or QueryExpressionSyntax)
            {
                return null;
            }
        }

        return null;
    }

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
    private LoopState ReturnedHere(ExpressionSyntax call, LoopState result) =>
        result.Objects.IsEmpty && this.region.TypeOf(call) is { } type && this.CanBeAnObject(type)
            ? this.MadeHere(place: call, state: result, exact: false)
            : result;

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
    private void Change(LoopState owner, Func<LoopState, LoopState> change, bool replaces, SyntaxNode? turn)
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

                bool sure =
                    now.Length == 1
                    && made.IsFrom(this.Frame)
                    && !made.IsOfTurnsBefore
                    && (made.Turn == null ? replaces : made.Turn == turn);

                this.region.Objects.Set(found: made, state: sure ? changed : old.Join(changed));
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
            // A cast that an operator of this compilation makes gives what the operator returns.
            CastExpressionSyntax c => this.FollowExpression(c) is { } converted
                ? converted.State.Join(this.TypeState(expression))
                : this.Eval(c.Expression),
            CheckedExpressionSyntax c => this.Eval(c.Expression),
            AwaitExpressionSyntax a => this.Eval(a.Expression),
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } s =>
                this.Eval(s.Operand),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } b => this.Eval(b.Left),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b =>
                this.Eval(b.Left).Join(this.Eval(b.Right)),
            ConditionalExpressionSyntax c => this.Eval(c.WhenTrue).Join(this.Eval(c.WhenFalse)),
            // The value of a?.b is the value of .b, whose owner is a.
            ConditionalAccessExpressionSyntax c => this.Eval(c.WhenNotNull),
            SwitchExpressionSyntax s => this.JoinAll(s.Arms.Select(static arm => arm.Expression)),
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
                this.Eval(position).Join(this.TypeState(expression)),
            ElementAccessExpressionSyntax e => this.FollowExpression(e) is { } indexed
                ? indexed.State.Join(this.TypeState(expression))
                : ElementOf(collection: this.Eval(e.Expression), type: this.region.TypeOf(expression))
                    .Join(this.TypeState(expression)),
            // A lambda is a value. Its code runs when something calls it.
            AnonymousFunctionExpressionSyntax function =>
                LoopState.OfFunction(new FunctionValue(declaration: function, definer: this, receiver: null)),
            // The object that the code is in, with the members that the evaluator knows.
            ThisExpressionSyntax when this.thisMembers.Count > 0 =>
                LoopState.None.WithMembers(
                    Construction.Of(this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default))),
            InvocationExpressionSyntax inv => this.EvalInvocation(inv),
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => this.EvalCreation(expression),
            IdentifierNameSyntax
                or MemberAccessExpressionSyntax
                or MemberBindingExpressionSyntax
                or GenericNameSyntax => this.EvalName(expression),
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
            : collection.AsElement;
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

    // The expression in parentheses, in a cast, or before the operator that says not null.
    private static ExpressionSyntax Unwrapped(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax around:
                    expression = around.Expression;
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

    // The expression of the element that a read by a constant index gives, for a collection that
    // keeps its positions. For an array with two dimensions, the first index gives a row, and the
    // row contains the element.
    private ExpressionSyntax? PositionOf(ElementAccessExpressionSyntax access) =>
        this.ElementsOf(access.Expression) is { } all
        && this.region.ModelFor(access).GetConstantValue(access.ArgumentList.Arguments[0].Expression)
            is { Value: int index and >= 0 }
        && index < all.Count
            ? all[index]
            : null;

    /// <summary>
    ///     The expression of each element of the collection in a local, in
    ///     order, when the collection keeps its positions. Null for each other
    ///     expression.
    /// </summary>
    /// <remarks>
    ///     A collection keeps its positions when a literal made it and no code
    ///     can change it. Each use of the local in its block must be a read. A
    ///     read is an index, <c>Count</c> or <c>Length</c>, or the collection of a
    ///     foreach. <c>First</c> or <c>Last</c> of the platform with no argument
    ///     is a read also. Any other use can change the collection or give it a
    ///     second name. Then, the local has one state for all its elements.
    /// </remarks>
    private IReadOnlyList<ExpressionSyntax>? ElementsOf(SyntaxNode collection)
    {
        if (this.region.SymbolOf(collection) is not ILocalSymbol local)
        {
            return null;
        }

        if (this.Frame.Positions.TryGetValue(key: local, value: out IReadOnlyList<ExpressionSyntax>? known))
        {
            return known;
        }

        IReadOnlyList<ExpressionSyntax>? elements =
            this.LiteralOf(local) is (var written, var uses) && uses.All(this.IsARead) ? written : null;

        this.Frame.Positions[local] = elements;

        return elements;
    }

    // The elements of the literal that made a local, and each name of the local in its block.
    private (IReadOnlyList<ExpressionSyntax> Elements, IEnumerable<IdentifierNameSyntax> Uses)? LiteralOf(
        ILocalSymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
        && this.LiteralElements(made: made, type: local.Type) is { } written
        && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } scope
            ? (written,
                scope.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(name => name.Identifier.ValueText == local.Name
                                   && SymbolEqualityComparer.Default.Equals(
                                       x: this.region.SymbolOf(name),
                                       y: local)))
            : null;

    /// <summary>
    ///     True for a collection that has one element, as far as the code says.
    ///     The expression is a literal with one element, or a local that such a
    ///     literal made and whose number of elements no code can change.
    /// </summary>
    /// <remarks>
    ///     An array has the number of elements that it got. Thus, each use of an
    ///     array in a local keeps it, but an assignment to the local and a
    ///     <c>ref</c> to it. A list keeps it while each use is a read, a
    ///     <c>Sort</c>, or a <c>Reverse</c>.
    /// </remarks>
    private bool HasOneElement(ExpressionSyntax collection)
    {
        while (collection is ParenthesizedExpressionSyntax inner)
        {
            collection = inner.Expression;
        }

        switch (collection)
        {
            // A collection expression in a call has one element for each type that the compiler
            // makes from it. A spread has the number of elements of its collection.
            case CollectionExpressionSyntax { Elements.Count: 1 } written:
                return written.Elements[0] is not SpreadElementSyntax spread || this.HasOneElement(spread.Expression);
            case ConditionalExpressionSyntax either:
                return this.HasOneElement(either.WhenTrue) && this.HasOneElement(either.WhenFalse);
            case SwitchExpressionSyntax { Arms.Count: > 0 } chosen:
                return chosen.Arms.All(arm => this.HasOneElement(arm.Expression));
        }

        if (this.region.SymbolOf(collection) is not ILocalSymbol local)
        {
            return this.LiteralElements(made: collection, type: this.region.TypeOf(collection)) is { Count: 1 };
        }

        if (!this.Frame.HasOneElement.TryGetValue(key: local, value: out bool known))
        {
            known =
                this.LiteralOf(local) is ({ Count: 1 }, var uses)
                && uses.All(local.Type is IArrayTypeSymbol ? static name => !IsWritten(name) : this.KeepsTheNumber);

            this.Frame.HasOneElement[local] = known;
        }

        return known;
    }

    // A read of a list, or a call of its Sort or its Reverse, which change only the order.
    private bool KeepsTheNumber(IdentifierNameSyntax name) =>
        this.IsARead(name)
        || (name.Parent is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Sort" or "Reverse", Parent: InvocationExpressionSyntax call
            }
            && this.region.SymbolOf(call)
                is IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" or "ArrayList" } owner }
            && SodaFlowSymbols.IsPlatform(owner));

    // The elements of a literal whose object has them in the order of the code. A spread has a
    // number of elements that the code does not say.
    private IReadOnlyList<ExpressionSyntax>? LiteralElements(ExpressionSyntax made, ITypeSymbol? type) =>
        made switch
        {
            CollectionExpressionSyntax collection
                when SodaFlowSymbols.KeepsTheOrder(type)
                     && collection.Elements.All(static element => element is ExpressionElementSyntax) =>
                [.. collection.Elements.Cast<ExpressionElementSyntax>().Select(static element => element.Expression)],
            InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ArrayInitializerExpression } initializer =>
                [.. initializer.Expressions],
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => [.. initializer.Expressions],
            ImplicitArrayCreationExpressionSyntax implied => [.. implied.Initializer.Expressions],
            // A List with no argument for its constructor, and an Add for each element.
            ObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 }, Initializer: { } added }
                when this.IsAListOf(creation: made, added: added) => [.. added.Expressions],
            ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: { } added }
                when this.IsAListOf(creation: made, added: added) => [.. added.Expressions],
            _ => null
        };

    // The initializer calls Add one time for each element. An element with braces gives the
    // arguments of one Add, and is not the expression of an element.
    private bool IsAListOf(ExpressionSyntax creation, InitializerExpressionSyntax added) =>
        added.Expressions.All(static element => element is not InitializerExpressionSyntax)
        && SodaFlowSymbols.KeepsTheOrder(this.region.TypeOf(creation));

    // The name is a name of the local, thus it is the owner in an index or in a member access.
    private bool IsARead(IdentifierNameSyntax name) =>
        name.Parent switch
        {
            ElementAccessExpressionSyntax access => !IsWritten(access),
            // The property. A method with the name Count can be one of this compilation.
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "Length" } member =>
                member.Parent is not InvocationExpressionSyntax,
            // A method of this compilation with the name First can change the collection.
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "First" or "Last",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } call
            } =>
                this.region.SymbolOf(call) is IMethodSymbol { ReducedFrom.ContainingType: { } owner }
                && SodaFlowSymbols.IsLinqEnumerable(owner),
            ForEachStatementSyntax => true,
            _ => false
        };

    // True when the code can change the element that an element access names. The code changes
    // it with an assignment, an increment, a ref or out argument, or a ref to the element.
    private static bool IsWritten(SyntaxNode element)
    {
        // A target of a deconstruction is in a tuple on the left of an assignment.
        while (true)
        {
            switch (element.Parent)
            {
                case ParenthesizedExpressionSyntax around:
                    element = around;
                    continue;
                case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
                    element = tuple;
                    continue;
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left == element;
                case ArgumentSyntax argument:
                    return !argument.RefKindKeyword.IsKind(SyntaxKind.None);
                case RefExpressionSyntax:
                    return true;
                case PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax:
                    return element.Parent.Kind()
                        is SyntaxKind.PreIncrementExpression
                        or SyntaxKind.PreDecrementExpression
                        or SyntaxKind.PostIncrementExpression
                        or SyntaxKind.PostDecrementExpression;
                default:
                    return false;
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

                LoopState named =
                    !this.assigned.Contains(symbol)
                    && this.bound.TryGetValue(key: symbol, value: out LoopState? boundState)
                        ? boundState
                        : LoopState.None;

                if (this.locals.TryGetValue(key: symbol, value: out LoopState? localState))
                {
                    named = named.Join(localState);
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
                    // its owner holds.
                    if (SodaFlowSymbols.ReadMembers.Contains(property.Name)
                        && this.region.Symbols.IsSodaFlow(property))
                    {
                        return this.Eval(
                                expression switch
                                {
                                    MemberAccessExpressionSyntax read => read.Expression,
                                    MemberBindingExpressionSyntax read => ConditionalReceiver(read),
                                    _ => null
                                })
                            .Content
                            .Join(this.TypeState(expression));
                    }
                }

                // A field that the code set while the loop is open, on an object that the walk does
                // not know.
                LoopState field = this.region.HasFields ? this.region.FieldState(symbol) : LoopState.None;

                // A member of this object, in a constructor the evaluator follows, has the state the
                // constructor gave it.
                if (expression is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
                {
                    return (this.thisMembers.TryGetValue(key: symbol.OriginalDefinition, value: out LoopState? own)
                            ? own
                            : LoopState.None)
                        .Join(field)
                        .Join(this.TypeState(expression));
                }

                LoopState fromOwner = field;

                ExpressionSyntax? ownerExpression =
                    expression switch
                    {
                        MemberAccessExpressionSyntax access => access.Expression,
                        MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
                        _ => null
                    };

                if (ownerExpression != null)
                {
                    LoopState owner = this.Eval(ownerExpression);

                    if (this.InMember(owner: owner, member: symbol, expression: expression) is { } inMember)
                    {
                        return inMember;
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
                        return owner.Members.StateThrough(symbol)
                            .FromIfUnknown(owner.Origin)
                            .Join(this.TypeState(expression));
                    }

                    // Otherwise, a member of a pending value is pending. Such a member is the cell
                    // behind a bindable, one element of a tuple from a loop block, or the values of
                    // a dictionary. A member that is not a sequence is one of the values that the
                    // owner contains, as the value of a pair is.
                    ITypeSymbol memberType = symbol is IPropertySymbol p ? p.Type : ((IFieldSymbol)symbol).Type;

                    // SyncRoot of a collection is an object to lock, and not a value in it.
                    if (symbol.ContainingType.IsTupleType
                        || (symbol.Name != "SyncRoot"
                            && this.region.Symbols.CanHoldState(
                                type: memberType,
                                compilation: this.region.Assembly)))
                    {
                        fromOwner = fromOwner.Join(SodaFlowSymbols.IsSequence(memberType) ? owner : owner.AsElement);
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
            }
            && this.ElementsOf(ends.Expression) is { Count: > 0 } all)
        {
            return this.Eval(method.Name == "First" ? all[0] : all[all.Count - 1])
                .Join(this.TypeState(invocation));
        }

        // Code that is not the library. A body in this compilation gives the state. Code that
        // cannot know the library only moves a value, and its result has the state of its inputs.
        // Any other callee is unknown, and a delegate with no known function is one. The result
        // gets no state, and the walk reports a pending argument with SF1003. A warning is for
        // what the analyzer can see.
        if (!this.region.Symbols.IsSodaFlow(method))
        {
            LoopState result =
                this.Classify(call: invocation, method: method) == CalleeKind.Mover
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

        LoopState state =
            OperatorTable.Rule(symbols: this.region.Symbols, method: method, receiverType: receiverType) switch
            {
                // A function that is not written at the call is a lambda in a local, a local
                // function, or a method. The evaluator follows it.
                OperatorRule.RunsNow => firstArgument.Join(this.Follow(invocation)?.State ?? LoopState.None),
                // A read gives the value that the receiver holds, with the state of that value.
                OperatorRule.Read => receiverState.Content,
                OperatorRule.SampleLazy => new LoopState(
                    pending: receiverState.Pending,
                    linked: false,
                    contentPending: false,
                    contentLinked: false),
                OperatorRule.MapLazy => receiverState.WithLinked(false),
                OperatorRule.MapStream => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.LambdaContent(invocation: invocation, parameterState: receiverState.Content)),
                // The result holds what the function returns, and not what the receiver held.
                OperatorRule.MapValue => new LoopState(
                    pending: receiverState.Pending,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(
                    this.LambdaContent(invocation: invocation, parameterState: receiverState.Content)),
                OperatorRule.LiftLazy => inputs.WithLinked(false),
                OperatorRule.Combine => new LoopState(
                    pending: inputs.Pending,
                    linked: inputs.Linked,
                    contentPending: false,
                    contentLinked: false),
                OperatorRule.SwitchValue => new LoopState(
                    pending: receiverState.Pending || receiverState.ContentPending,
                    linked: receiverState.Linked || receiverState.ContentLinked,
                    contentPending: false,
                    contentLinked: false),
                OperatorRule.SwitchStream => new LoopState(
                    pending: false,
                    linked: receiverState.ContentLinked,
                    contentPending: false,
                    contentLinked: false),
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
                    contentLinked: false),
                OperatorRule.ConstantLazy => firstArgument.WithLinked(false),
                // A value given to a constant or a sink is content. The links of a value with members
                // of its own are in it, and not links of the cell.
                OperatorRule.Content => firstArgument.AsContent,
                OperatorRule.Fold => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false),
                OperatorRule.Updates => source.WithPending(false),
                OperatorRule.MapTo => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(firstArgument),
                // A snapshot samples its cells at each firing. Only the stream has a link.
                OperatorRule.StreamFromReceiver => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: receiverState.ContentPending,
                    contentLinked: receiverState.ContentLinked),
                OperatorRule.MergeStreams => new LoopState(
                    pending: false,
                    linked: LinkedStreamInput(
                        symbols: this.region.Symbols,
                        receiverState: receiverState,
                        receiverType: receiverType,
                        argumentStates: argumentStates,
                        argumentTypes: argumentTypes),
                    contentPending: inputs.ContentPending,
                    contentLinked: inputs.ContentLinked),
                OperatorRule.DeferLazy => new LoopState(
                    pending: firstArgument.Pending,
                    linked: false,
                    contentPending: false,
                    contentLinked: false),
                // A defer, a split, a timer, a debounce, a listener, and a send: no state.
                OperatorRule.Break => LoopState.None,
                OperatorRule.Default => DefaultLibraryResult(
                    symbols: this.region.Symbols,
                    method: method,
                    inputs: inputs),
                _ => throw new ArgumentOutOfRangeException(
                    paramName: nameof(invocation),
                    message: "An operator rule has no case.")
            };

        // The function of a map, a lift, or a view computes the value of the result at the first
        // read of it. A function that reads a pending value makes that value pending. A view that
        // sorts by the order in a cell runs the function of that order.
        if (!state.Pending
            && (sortsByPendingOrder
                || (OperatorTable.ComputesValueWithFunction(
                        symbols: this.region.Symbols,
                        method: method,
                        receiverType: receiverType)
                    && this.CanUseState(invocation: invocation, given: inputs.Content)
                    && this.Quiet()
                        .FunctionReadsPending(invocation: invocation, method: method, given: inputs.Content))))
        {
            state = state.WithPending(true);
        }

        // The result of an operator came into this code as its inputs did.
        return state.FromIfUnknown(inputs.Origin);
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
                .Join(this.MovedInto(creation));

        if (initializer != null)
        {
            state = this.WithInitializer(state: state, initializer: initializer);
        }

        // An object of code that cannot know the library is not an object of this compilation.
        // Thus, it contains the objects that have the members, and is not one of them.
        return this.MadeHere(
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

        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            return this.Follow(call) != null ? CalleeKind.Body : CalleeKind.Unknown;
        }

        IMethodSymbol definition =
            call is InvocationExpressionSyntax invocation
                ? this.DefinitionsOf(invocation: invocation, method: method)[0]
                : method.OriginalDefinition;

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
    public LoopState Moved(InvocationExpressionSyntax invocation, IMethodSymbol method, bool withReceiver)
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
            seed: seed);
    }

    private LoopState MovedArguments(
        IMethodSymbol definition,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int offset,
        LoopState seed)
    {
        // A callee that returns a delegate gives back the function that it got, as a wrapper
        // does. Then, the function is a value that it moves. A Lazy keeps the function that its
        // constructor gets. A method that returns a Lazy gives its function to that Lazy.
        // A comparer from a function, as from Comparer.Create, has that function.
        bool keepsFunctions =
            definition.ReturnType.TypeKind == TypeKind.Delegate
            || SodaFlowSymbols.IsLazy(definition.ReturnType)
            || SodaFlowSymbols.IsComparer(definition.ReturnType)
            || (definition.MethodKind == MethodKind.Constructor && SodaFlowSymbols.IsLazy(definition.ContainingType));

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
            .Aggregate(
                seed: seed,
                func: (moved, given) =>
                    moved.Join(this.Eval(given.Written))
                        .Join(this.Enumerated(written: given.Written, parameter: given.Parameter?.Type)));
    }

    /// <summary>
    ///     The location of an exchange and the value that the callee puts
    ///     there, or null when the call is not an exchange.
    /// </summary>
    public static (ExpressionSyntax Location, ExpressionSyntax Value)? ExchangeOf(
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
            }
        }

        return location != null && value != null ? (location, value) : null;
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

        return Returned(state: result, type: method.ReturnType, takesOneOut: takesOneOut);
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
            return takesOneOut || !Construction.SaysNothing(type) ? state.AsElement : state;
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
    ///     after the call, as <c>Lazy</c> and a command object do. A method runs a
    ///     function given for a delegate parameter. It does not when it returns a
    ///     delegate or a <c>Lazy</c>, which then has the function. A function given
    ///     for a parameter of a different type, as to <c>Add</c> of a list, is a
    ///     value that the callee keeps. A read of <c>Value</c> runs the function
    ///     that a <c>Lazy</c> has, which <see cref="FollowLazy" /> follows. A
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
                seed: LoopState.None)
            : LoopState.None;
    }

    private Followed? FollowInvocation(InvocationExpressionSyntax invocation)
    {
        if (this.region.SymbolOf(invocation) is not IMethodSymbol method)
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        // A call of a delegate runs the functions that the delegate can be.
        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            ExpressionSyntax target = this.DelegateOf(invocation);
            LoopState targetState = this.Eval(target);

            return targetState.Functions.IsEmpty
                ? null
                : this.FollowFunctions(
                    call: invocation,
                    name: NameOf(target),
                    functions: targetState.Functions,
                    target: targetState,
                    arguments: arguments);
        }

        if (this.region.Symbols.IsSodaFlow(method))
        {
            return OperatorTable.FunctionsInTheCall.Contains(OperatorName(method))
                ? this.FollowGivenFunctions(invocation: invocation, method: method)
                : null;
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

        (Dictionary<ISymbol, LoopState> members, LoopState? receiverState) =
            this.MembersFor(isStatic: definition.IsStatic, owner: receiver);

        return this.Enter(
            call: invocation,
            name: method.Name,
            body: body,
            bindings: bindings,
            definer: null,
            members: members,
            receiver: receiverState,
            target: null);
    }

    /// <summary>
    ///     The definition of the method that a call runs. The call can be through
    ///     an interface or a base type, on an object whose constructor the evaluator
    ///     followed. Then, it runs the method of the type of that object. A value
    ///     that is one of two objects gives the method of each.
    /// </summary>
    private IReadOnlyList<IMethodSymbol> DefinitionsOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (method is not { IsStatic: false, ReducedFrom: null }
            || ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method) is not { } receiver
            || this.Self(receiver).Members is not { } members)
        {
            return [definition];
        }

        IMethodSymbol[] actual =
        [
            .. members.Resolve(definition).OfType<IMethodSymbol>().Select(static found => found.OriginalDefinition)
        ];

        return actual.Length == 0 ? [definition] : actual;
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
                || !RunsFunction(kind: kind, method: method, parameter: parameter))
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
                                each: (written, given))));

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
                    outs: first.Outs.SetItems(second.Outs));

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
    ///     parameters gets.
    /// </param>
    private Followed? FollowFunction(
        SyntaxNode call,
        string name,
        FunctionValue function,
        LoopState target,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        (ExpressionSyntax Written, LoopState Given)? each)
    {
        SemanticModel model = this.region.ModelFor(function.Declaration);

        IReadOnlyList<Binding> Bindings(ImmutableArray<IParameterSymbol> parameters) =>
            each is (var written, var given)
                ?
                [
                    .. parameters.Select(parameter =>
                        new Binding(parameter: parameter, argument: written, state: given))
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
                    target: target);
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
                        target: target);
                }

                LoopState owner = LoopState.None.WithMembers(function.Receiver);

                return this.Enter(
                    call: call,
                    name: name,
                    body: body,
                    bindings: bindings,
                    definer: null,
                    members: TaggedMembers(owner),
                    receiver: owner,
                    target: null);
            }
            default:
                return null;
        }
    }

    private Followed? FollowCreation(SyntaxNode creation, BaseArgumentListSyntax? arguments) =>
        this.region.SymbolOf(creation) is IMethodSymbol constructor
        && !this.region.Symbols.IsSodaFlow(constructor)
            ? this.FollowConstructor(
                call: creation,
                constructor: constructor,
                arguments: arguments?.Arguments ?? default)
            : null;

    /// <summary>
    ///     The constructor that a constructor calls first: the constructor of the
    ///     base, or a second constructor of the type. The members that it sets are
    ///     members of the object that this code is in.
    /// </summary>
    private Followed? FollowChain(SyntaxNode chain, BaseArgumentListSyntax? arguments)
    {
        if (this.region.SymbolOf(chain) is not IMethodSymbol constructor
            || this.region.Symbols.IsSodaFlow(constructor))
        {
            return null;
        }

        Followed? first =
            this.FollowConstructor(
                call: chain,
                constructor: constructor,
                arguments: arguments?.Arguments ?? default);

        if (first?.State.Members != null)
        {
            foreach (KeyValuePair<ISymbol, LoopState> pair in first.State.Members.Members)
            {
                this.SetMember(member: pair.Key, state: pair.Value);
            }
        }

        return first;
    }

    private Followed? FollowConstructor(
        SyntaxNode call,
        IMethodSymbol constructor,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
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
            this.Bind(definition: definition, arguments: arguments, extensionReceiver: null);

        // A constructor that gets no state gives no member a state, and the evaluator does not
        // read it. The object keeps its type, because the code can set a member of it later.
        // A constructor that gets an object of the store can keep it, which gives the object a
        // second name. The evaluator reads such a constructor while ReadsForObjects permits.
        bool noState = !this.region.HasFields && bindings.All(static binding => binding.State.IsNone);

        switch (noState)
        {
            case true when !this.ReadsForObjects(bindings):
                return new Followed(
                    state: LoopState.None.WithMembers(Construction.Of(definition.ContainingType)),
                    findings: Followed.Nothing.Findings,
                    outs: Followed.Nothing.Outs);
            case false when this.depth >= MaxInlineDepth || !this.region.TryEnter():
                return null;
        }

        Dictionary<ISymbol, LoopState> members = new(SymbolEqualityComparer.Default);

        LoopStateEvaluator inner =
            this.Inner(call: call, bindings: bindings, definer: null, members: members, function: null);

        List<Finding> found = [];
        SinkWalker walker = new(region: this.region, evaluator: inner, sink: found.Add);
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

        // The initializers of the members run in each constructor, but not in one that calls a
        // second constructor of the type first.
        if (chain is not ConstructorInitializerSyntax { RawKind: (int)SyntaxKind.ThisConstructorInitializer })
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
            leaving: leaving);

        void Set(ISymbol? member, ExpressionSyntax value)
        {
            if (member == null)
            {
                return;
            }

            walker.Visit(value);
            inner.SetMember(member: member, state: inner.Eval(value));
        }
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
        return PropertiesOf(owner: this.Self(owner).Members, written: written)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, property) =>
                {
                    if (GetterOf(property.OriginalDefinition) is not { } body)
                    {
                        return all;
                    }

                    (Dictionary<ISymbol, LoopState> members, LoopState? receiver) =
                        this.MembersFor(isStatic: property.IsStatic, owner: owner);

                    return Both(
                        first: all,
                        second: this.Enter(
                            call: access,
                            name: property.Name,
                            body: body,
                            bindings: [],
                            definer: null,
                            members: members,
                            receiver: receiver,
                            target: null));
                });
    }

    private static IEnumerable<IPropertySymbol> PropertiesOf(Construction? owner, IPropertySymbol written)
    {
        IPropertySymbol[] actual = [.. owner?.Resolve(written).OfType<IPropertySymbol>() ?? []];

        return actual.Length == 0 ? [written] : actual;
    }

    /// <summary>
    ///     A call that the code writes with no call syntax. The compiler binds an
    ///     operator, a conversion, an accessor, a <c>Deconstruct</c>, and some
    ///     other forms to a member. The evaluator reads that member as a call of
    ///     it. The result is null for a member with no body to read.
    /// </summary>
    /// <param name="call">The node that makes the call. A report about a member is at this node.</param>
    /// <param name="member">The method or the accessor that the node calls.</param>
    /// <param name="owner">
    ///     The state of the object, for an instance member. Null for the object
    ///     that this code is in.
    /// </param>
    /// <param name="arguments">
    ///     The expressions that the member gets, in the order of its parameters,
    ///     each with its state.
    /// </param>
    public Followed? FollowImplied(
        SyntaxNode call,
        IMethodSymbol member,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        if (this.implied.TryGetValue(key: (call, member), value: out Followed? known))
        {
            return known;
        }

        // The member of the object, when the code names one of an interface or a base type. A
        // value that is one of two objects has the member of each.
        IMethodSymbol[] actual =
        [
            .. owner?.Members?.Resolve(member.OriginalDefinition).OfType<IMethodSymbol>() ?? []
        ];

        Followed? result =
            (actual.Length == 0 ? [member] : actual)
            .Select(static found => found.OriginalDefinition)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, definition) =>
                    Both(
                        first: all,
                        second: this.FollowImpliedDefinition(
                            call: call,
                            definition: definition,
                            owner: owner,
                            arguments: arguments)));

        this.implied[(call, member)] = result;

        return result;
    }

    private Followed? FollowImpliedDefinition(
        SyntaxNode call,
        IMethodSymbol definition,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        Followed? result = null;

        if (!this.region.Symbols.IsSodaFlow(definition) && BodyOf(definition) is { } body)
        {
            IReadOnlyList<Binding> bindings =
            [
                .. arguments
                    .Take(definition.Parameters.Length)
                    .Select((argument, position) =>
                        new Binding(
                            parameter: definition.Parameters[position],
                            argument: argument.Written,
                            state: argument.State))
            ];

            result =
                definition.IsStatic
                    ? this.Enter(
                        call: call,
                        name: ImpliedNameOf(definition),
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                        receiver: null,
                        target: null)
                    : this.Enter(
                        call: call,
                        name: ImpliedNameOf(definition),
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: owner == null ? this.thisMembers : TaggedMembers(owner),
                        receiver: owner,
                        target: null);
        }

        return result;
    }

    /// <summary>
    ///     The call that an expression makes with no call syntax. It is the getter
    ///     of an indexer, an operator, or a conversion that the code writes as a
    ///     cast. A concatenation also calls <c>ToString</c>. The result is null for
    ///     an expression with no such call.
    /// </summary>
    public Followed? FollowExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ElementAccessExpressionSyntax access when this.region.Symbols.DeclaresIndexers =>
                this.FollowIndexer(access: access, owner: access.Expression, indexes: access.ArgumentList),
            ElementBindingExpressionSyntax binding
                when this.region.Symbols.DeclaresIndexers && ConditionalReceiver(binding) is { } owner =>
                this.FollowIndexer(access: binding, owner: owner, indexes: binding.ArgumentList),
            BinaryExpressionSyntax binary
                when this.region.Symbols.DeclaresOperators || this.region.Symbols.DeclaresToString =>
                this.region.SymbolOf(binary) switch
                {
                    IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } operation =>
                        this.FollowOperator(call: binary, operation: operation, operands: [binary.Left, binary.Right]),
                    // A concatenation calls ToString on an operand that is not a string.
                    IMethodSymbol
                    {
                        MethodKind: MethodKind.BuiltinOperator,
                        ContainingType.SpecialType: SpecialType.System_String
                    } => Both(first: this.FollowToString(binary.Left), second: this.FollowToString(binary.Right)),
                    _ => null
                },
            PrefixUnaryExpressionSyntax prefix when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(call: prefix, operation: this.region.SymbolOf(prefix), operands: [prefix.Operand]),
            PostfixUnaryExpressionSyntax postfix when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(
                    call: postfix,
                    operation: this.region.SymbolOf(postfix),
                    operands: [postfix.Operand]),
            CastExpressionSyntax cast when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(call: cast, operation: this.region.SymbolOf(cast), operands: [cast.Expression]),
            AssignmentExpressionSyntax compound
                when this.region.Symbols.DeclaresOperators
                     && !compound.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                this.FollowOperator(
                    call: compound,
                    operation: this.region.SymbolOf(compound),
                    operands: [compound.Left, compound.Right]),
            _ => null
        };

    /// <summary>
    ///     The conversion that the compiler adds to an expression, when an
    ///     operator of this compilation makes it.
    /// </summary>
    public Followed? FollowConversion(ExpressionSyntax expression) =>
        this.region.Symbols.DeclaresOperators && this.region.ConversionOf(expression) is { } conversion
            ? this.FollowOperator(call: expression, operation: conversion, operands: [expression])
            : null;

    /// <summary>
    ///     The call of <c>operator true</c> or <c>operator false</c> that a
    ///     condition makes on its value. No node names that operator, thus the
    ///     evaluator asks for the operation of the condition. It asks only in a
    ///     compilation that declares such an operator.
    /// </summary>
    public Followed? FollowTruth(ExpressionSyntax condition) =>
        this.region.Symbols.DeclaresTruth
        && this.region.ModelFor(condition).GetOperation(condition)?.Parent
            is IUnaryOperation { OperatorMethod: { } truth }
            ? this.FollowOperator(call: condition, operation: truth, operands: [condition])
            : null;

    /// <summary>
    ///     The call of <c>ToString</c> that an interpolation or a concatenation
    ///     makes on an object of this compilation.
    /// </summary>
    public Followed? FollowToString(ExpressionSyntax written) =>
        this.region.Symbols is { DeclaresToString: true, ObjectToString: { } toString }
        && this.Self(written) is { Members: not null } owner
            ? this.FollowImplied(call: written, member: toString, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The call of <c>Dispose</c> that a <c>using</c> makes on an object of
    ///     this compilation, when the code leaves the statement or the block.
    /// </summary>
    public Followed? FollowDispose(ExpressionSyntax resource) =>
        this.region.Symbols is { DeclaresDispose: true, DisposableDispose: { } dispose }
        && this.Self(resource) is { Members: not null } owner
            ? this.FollowImplied(call: resource, member: dispose, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The members of objects of this compilation that a call to code of the
    ///     platform runs, with no call of them in this code.
    ///     <see cref="PlatformCallers" /> says which members a method calls. A
    ///     callee that gets a sequence of this compilation also enumerates it.
    /// </summary>
    /// <remarks>
    ///     A report about such a member is at the value that has it, and not at
    ///     the call. One call can give two values, and each has its own members.
    /// </remarks>
    public Followed? FollowPlatformCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!this.region.Symbols.DeclaresPlatformMembers)
        {
            return null;
        }

        ExpressionSyntax? receiver = ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);
        method = this.ImplementationOf(method: method, receiver: receiver);
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        List<Operand> operands = [];

        // In a static call of an extension method, the receiver is one of the arguments. In a call
        // on the receiver, it is the argument of the first parameter.
        if (receiver != null && arguments.All(argument => argument.Expression != receiver))
        {
            operands.Add(
                method.ReducedFrom != null
                    ? new Operand(written: receiver, parameter: definition.Parameters[0].Type, isReceiver: false)
                    : new Operand(written: receiver, parameter: null, isReceiver: true));
        }

        // The callee does not compare a value that it only keeps.
        operands.AddRange(
            arguments
                .Select(static (argument, position) => (Argument: argument, Position: position))
                .Where(pair =>
                    !PlatformCallers.IsOnlyKept(
                        ParameterFor(method: method, argument: pair.Argument, position: pair.Position)))
                .Select(pair =>
                    new Operand(
                        written: pair.Argument.Expression,
                        parameter: this.KnownTypeFor(method: method, argument: pair.Argument, position: pair.Position),
                        isReceiver: false)));

        PlatformCallbacks calls = PlatformCallers.Of(method);

        // A tuple in a local of a type that says nothing, such as object. The call is the one of
        // the tuple, which calls the same member of each element.
        if (calls == PlatformCallbacks.None
            && receiver != null
            && this.Eval(receiver).Members is { IsTuple: true })
        {
            calls = PlatformCallers.OfTuple(definition.Name);
        }

        return this.FollowPlatformAndItsValues(
            method: method,
            calls: calls,
            operands: operands,
            keeper: method.ReducedFrom == null ? receiver : null,
            also: LoopState.None,
            given: () => this.Moved(invocation: invocation, method: method, withReceiver: true).AsElement,
            returned: () => this.Follow(invocation)?.State ?? LoopState.None,
            firstIsStored: true);
    }

    /// <summary>
    ///     The method that a call through an interface of the platform runs, when
    ///     the code says the type of the object. The method of the interface
    ///     otherwise.
    /// </summary>
    /// <remarks>
    ///     A member of an interface has a row only when each collection of the
    ///     platform compares in it. <c>Add</c> of <c>ICollection</c> has no row,
    ///     because a list adds with no comparison. Thus, the row of the method of
    ///     the type is more correct. When that method has no row, the row of the
    ///     interface member holds.
    /// </remarks>
    private IMethodSymbol ImplementationOf(IMethodSymbol method, ExpressionSyntax? receiver) =>
        receiver != null
        && method.ContainingType is { TypeKind: TypeKind.Interface } contract
        && SodaFlowSymbols.IsPlatform(contract)
        && this.KnownTypeOf(receiver) is INamedTypeSymbol type
        && type.FindImplementationForInterfaceMember(method) is IMethodSymbol implemented
        && SodaFlowSymbols.IsPlatform(implemented.ContainingType)
        && PlatformCallers.Of(implemented) != PlatformCallbacks.None
            ? implemented
            : method;

    /// <summary>
    ///     The type of the object that an expression gives, when it is a class
    ///     or a struct. A cast does not change the object. A local that no code
    ///     changes after its declaration has the type of the value that it got.
    ///     A call of a method of this compilation has the type that each of its
    ///     returns gives.
    /// </summary>
    /// <param name="written">The expression.</param>
    /// <param name="depth">The number of locals and calls that the evaluator went through before this one.</param>
    private ITypeSymbol? KnownTypeOf(ExpressionSyntax written, int depth = 0)
    {
        // A local can get its value from a second local.
        for (; depth <= MaxKnownTypeDepth; depth++)
        {
            ExpressionSyntax inner = Unwrapped(written);

            if (this.region.TypeOf(inner)
                is
                {
                    TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Array,
                    SpecialType: not SpecialType.System_Object
                } known)
            {
                return known;
            }

            if (inner is InvocationExpressionSyntax call)
            {
                return this.ReturnedTypeOf(call: call, depth: depth + 1);
            }

            if (inner is not IdentifierNameSyntax name
                || this.region.SymbolOf(name) is not ILocalSymbol local
                || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is not VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                || NamesOf(local: local, declarator: declarator, region: this.region) is not { } uses
                || !uses.All(static use => !IsWritten(use)))
            {
                return null;
            }

            written = made;
        }

        return null;
    }

    /// <summary>
    ///     The type of the object that a call of a method of this compilation
    ///     gives. It is known when each return of the method gives an object of
    ///     that type. A return of a parameter gives the object of the argument,
    ///     when the method does not change that parameter.
    /// </summary>
    /// <remarks>
    ///     A method that a type can override can run a different body. Thus, it
    ///     gives no type.
    /// </remarks>
    private ITypeSymbol? ReturnedTypeOf(InvocationExpressionSyntax call, int depth)
    {
        if (depth > MaxKnownTypeDepth
            || this.region.SymbolOf(call) is not IMethodSymbol { ReducedFrom: null } method
            || method is { IsVirtual: true, ContainingType.IsSealed: false }
            || method is { IsOverride: true, IsSealed: false, ContainingType.IsSealed: false }
            || method.OriginalDefinition is not { } definition
            || BodyOf(definition) is not { } body)
        {
            return null;
        }

        ITypeSymbol? type = null;
        IEnumerable<ExpressionSyntax> returns = body is ExpressionSyntax expression ? [expression] : ReturnExpressions(body);

        foreach (ExpressionSyntax given in returns.Select(returned =>
                     this.ArgumentFor(call: call, definition: definition, returned: returned) ?? returned))
        {
            if (this.KnownTypeOf(written: given, depth: depth) is not { } one
                || (type != null && !SymbolEqualityComparer.Default.Equals(x: type, y: one)))
            {
                return null;
            }

            type = one;
        }

        return type;
    }

    // The argument of a call for the parameter that a return of the callee gives. Null when the
    // return gives no parameter, or when the callee writes that parameter.
    private ExpressionSyntax? ArgumentFor(
        InvocationExpressionSyntax call,
        IMethodSymbol definition,
        ExpressionSyntax returned)
    {
        if (Unwrapped(returned) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not IParameterSymbol parameter
            || IsWrittenIn(parameter: parameter, region: this.region))
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        return arguments
            .Where((argument, position) =>
                SymbolEqualityComparer.Default.Equals(
                    x: SodaFlowSymbols.ParameterOf(callee: definition, argument: argument, position: position),
                    y: parameter))
            .Select(static argument => argument.Expression)
            .FirstOrDefault();
    }

    // True for a parameter that the code of its method writes.
    private static bool IsWrittenIn(ISymbol parameter, LoopRegion region) =>
        parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().Parent?.Parent is not { } declaration
        || declaration.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(name => name.Identifier.ValueText == parameter.Name
                         && IsWritten(name)
                         && SymbolEqualityComparer.Default.Equals(x: region.SymbolOf(name), y: parameter));

    // Each name of a local in the block that declares it. Null for a local of the top-level
    // statements, which have no block.
    private static IEnumerable<IdentifierNameSyntax>? NamesOf(
        ISymbol local,
        SyntaxNode declarator,
        LoopRegion region) =>
        declarator.FirstAncestorOrSelf<BlockSyntax>()
            ?.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == local.Name
                           && SymbolEqualityComparer.Default.Equals(x: region.SymbolOf(name), y: local));

    /// <summary>The same as <see cref="FollowPlatformCall" />, for a constructor of the platform.</summary>
    public Followed? FollowPlatformCreation(IMethodSymbol constructor, SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        this.region.Symbols.DeclaresPlatformMembers
            ? this.FollowPlatformAndItsValues(
                method: constructor,
                calls: PlatformCallers.Of(constructor),
                operands:
                [
                    .. arguments.Select((argument, position) =>
                        new Operand(
                            written: argument.Expression,
                            parameter: SodaFlowSymbols.ParameterOf(
                                    callee: constructor.OriginalDefinition,
                                    argument: argument,
                                    position: position)
                                ?.Type,
                            isReceiver: false))
                ],
                keeper: null,
                also: LoopState.None,
                given: null,
                returned: null,
                firstIsStored: false)
            : null;

    /// <summary>
    ///     The same as <see cref="FollowPlatformCall" />, for a member of the
    ///     platform that the code calls with no call syntax. It is the
    ///     <c>Add</c> of a collection initializer or of a collection expression, or
    ///     an accessor of an indexer.
    /// </summary>
    /// <param name="member">The method or the accessor.</param>
    /// <param name="collection">The collection, which has the values that it got before.</param>
    /// <param name="values">The values that the member gets, in the order of its parameters.</param>
    /// <param name="alone">
    ///     True when the value is the only one of a new collection, as the one
    ///     element of a collection expression is. The member then has no second
    ///     object to compare it with.
    /// </param>
    /// <param name="others">
    ///     Each expression for a collection whose objects the collection has
    ///     before the values. That is the collection itself when code before made
    ///     it, an argument of its constructor, or a spread. The member can call a
    ///     member of one of those objects. The walk reads each value of a literal
    ///     where the literal gives it, thus the literal is not here.
    /// </param>
    /// <param name="before">
    ///     For an element of a literal: the values of each element that the
    ///     literal gave before it. The values of one element are in the order of
    ///     the parameters of the member. The collection has those objects when it
    ///     gets this element, and the member gives the call to one side, as a call
    ///     of it does. Null for a member that is not an element of a literal.
    /// </param>
    public Followed? FollowPlatformMember(
        IMethodSymbol member,
        ExpressionSyntax? collection,
        IEnumerable<ExpressionSyntax> values,
        bool alone,
        IEnumerable<ExpressionSyntax> others,
        IEnumerable<IEnumerable<ExpressionSyntax>>? before)
    {
        member = this.ImplementationOf(method: member, receiver: collection);

        // A member with no row can compare the values of a dictionary, as the indexer of a builder
        // of an immutable dictionary does.
        if (!this.region.Symbols.DeclaresKeyMembers
            || (PlatformCallers.Of(member) == PlatformCallbacks.None
                && PlatformCallers.ValueReceiverOf(member) == null))
        {
            return null;
        }

        ImmutableArray<IParameterSymbol> parameters = member.OriginalDefinition.Parameters;

        // Of an element before, the collection compares only the values that the member compares,
        // as the key of a pair of a dictionary.
        ImmutableHashSet<ITypeParameterSymbol>? compared =
            PlatformCallers.ComparedTypeParameters(method: member, ofValues: false);

        IEnumerable<ExpressionSyntax> earlier =
            (before ?? []).SelectMany(element =>
                element.Where((_, position) =>
                    compared == null
                    || position >= parameters.Length
                    || parameters[position].Type is not ITypeParameterSymbol parameter
                    || compared.Contains(parameter)));

        List<Operand> operands =
        [
            .. values.Select((value, position) =>
                new Operand(
                    written: value,
                    parameter: position < parameters.Length ? parameters[position].Type : null,
                    isReceiver: false)),
            .. earlier.Select(static value => new Operand(written: value, parameter: null, isReceiver: true)),
            .. others.Select(static other => new Operand(written: other, parameter: null, isReceiver: true))
        ];

        return this.FollowPlatformAndItsValues(
            method: member,
            calls: alone ? PlatformCallers.Of(member) & ~ComparesTwo : PlatformCallers.Of(member),
            operands: operands,
            keeper: collection,
            also: this.Eval(collection),
            given: null,
            returned: null,
            firstIsStored: false);
    }

    /// <summary>
    ///     The calls that code of the platform makes when it enumerates a sequence
    ///     of this compilation. The first is <c>GetEnumerator</c>. The others are
    ///     <c>MoveNext</c> and <c>Current</c> of an enumerator of this compilation.
    ///     The result has the state of the elements. It is null for a value that is
    ///     not such a sequence.
    /// </summary>
    /// <remarks>
    ///     A query of the platform enumerates its source when something
    ///     enumerates the query, which can be after the block. The analyzer reads
    ///     the call as the time, as it does for a function that a query gets.
    /// </remarks>
    public Followed? FollowEnumeration(ExpressionSyntax written)
    {
        if (!this.region.Symbols.DeclaresEnumerators
            || this.Self(written) is not { Members: { HasType: true } members } sequence)
        {
            return null;
        }

        // The method for the generic interface. The other one calls it. A value that is one of
        // two sequences has the method of each type.
        return members.Implementations(SodaFlowSymbols.IsEnumerableInterface)
            .GroupBy(
                keySelector: static method => method.ContainingType,
                comparer: SymbolEqualityComparer.Default)
            .Select(static ofOneType =>
                ofOneType.FirstOrDefault(static method =>
                    method.ReturnType is INamedTypeSymbol { IsGenericType: true })
                ?? ofOneType.First())
            .Aggregate(
                seed: (Followed?)null,
                func: (all, start) =>
                    Both(
                        first: all,
                        second: this.FollowEnumerationFrom(written: written, sequence: sequence, start: start)));
    }

    private Followed? FollowEnumerationFrom(SyntaxNode written, LoopState sequence, IMethodSymbol start)
    {
        if (this.FollowImplied(call: written, member: start, owner: sequence, arguments: []) is not { } enumerator)
        {
            return null;
        }

        // An iterator has no enumerator object of this compilation, and a method can give the
        // enumerator of a collection of the platform. The result then has the state of the
        // elements.
        if (enumerator.State.Members is not { } made || !made.Is(start.ReturnType))
        {
            return enumerator;
        }

        Followed? all = enumerator;
        LoopState elements = LoopState.None;

        foreach (IMethodSymbol member in made.Implementations(SodaFlowSymbols.IsEnumeratorInterface))
        {
            if (member.Name is "Reset" || !member.Parameters.IsEmpty)
            {
                continue;
            }

            Followed? step = this.FollowImplied(call: written, member: member, owner: enumerator.State, arguments: []);
            all = Both(first: all, second: step);

            if (member.MethodKind == MethodKind.PropertyGet)
            {
                elements = elements.Join(step?.State ?? LoopState.None);
            }
        }

        return new Followed(
            state: elements,
            findings: all?.Findings ?? ImmutableArray<Finding>.Empty,
            outs: Followed.Nothing.Outs);
    }

    /// <summary>
    ///     The call that the getter of an indexer of the platform makes on its
    ///     key, as the indexer of a dictionary does.
    /// </summary>
    public Followed? FollowPlatformIndexer(ElementAccessExpressionSyntax access) =>
        this.region.Symbols.DeclaresKeyMembers
        && this.region.SymbolOf(access)
            is IPropertySymbol { GetMethod: { DeclaringSyntaxReferences.IsEmpty: true } getter }
            ? this.FollowPlatformMember(
                member: getter,
                collection: access.Expression,
                values: access.ArgumentList.Arguments.Select(static index => index.Expression),
                alone: false,
                others: [access.Expression],
                before: null)
            : null;

    // True for the static member Empty of a collection of the platform, as
    // ImmutableDictionary<TKey, TValue>.Empty. A member of it compares nothing.
    private bool IsTheEmptyCollection(ExpressionSyntax written) =>
        Unwrapped(written) is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Empty" } access
        && this.region.SymbolOf(access) is IFieldSymbol { IsStatic: true } empty
        && SodaFlowSymbols.IsPlatform(empty.ContainingType);

    /// <summary>
    ///     The calls that a method of the platform makes for its row. A method that
    ///     also compares the values of a dictionary gets a second step for those.
    /// </summary>
    private Followed? FollowPlatformAndItsValues(
        IMethodSymbol method,
        PlatformCallbacks calls,
        IReadOnlyList<Operand> operands,
        ExpressionSyntax? keeper,
        LoopState also,
        Func<LoopState>? given,
        Func<LoopState>? returned,
        bool firstIsStored)
    {
        Followed? forTheRow =
            this.FollowPlatform(
                method: method,
                calls: calls,
                operands: operands,
                keeper: keeper,
                also: also,
                given: given,
                returned: returned,
                firstIsStored: firstIsStored,
                forValues: null);

        return PlatformCallers.ValueReceiverOf(method) is { } forValues
            ? Both(
                first: forTheRow,
                second: this.FollowPlatform(
                    method: method,
                    calls: calls,
                    operands: operands,
                    keeper: keeper,
                    also: also,
                    given: given,
                    returned: returned,
                    firstIsStored: firstIsStored,
                    forValues: forValues))
            : forTheRow;
    }

    /// <param name="method">The method of the platform.</param>
    /// <param name="calls">The members that the method calls on an object that it gets.</param>
    /// <param name="operands">The values that the method gets, with the owner of the call first.</param>
    /// <param name="keeper">The collection that keeps a comparer for the method, when there is one.</param>
    /// <param name="also">The state of one more object that a member can get as its argument.</param>
    /// <param name="given">The state of the values that the method gives to a function.</param>
    /// <param name="returned">The state of what a function that is not a lambda returns.</param>
    /// <param name="firstIsStored">
    ///     True when the first operand is the collection of the call, for a
    ///     method with no owner, as the array is for <c>Array.IndexOf</c>.
    /// </param>
    /// <param name="forValues">
    ///     Null for the calls of the row of the method. For a member of a
    ///     dictionary that also compares its values, a second step reads the call
    ///     again. Then, this is the receiver of those calls, and the step is for
    ///     <c>Equals</c> of the values only.
    /// </param>
    private Followed? FollowPlatform(
        IMethodSymbol method,
        PlatformCallbacks calls,
        IReadOnlyList<Operand> operands,
        ExpressionSyntax? keeper,
        LoopState also,
        Func<LoopState>? given,
        Func<LoopState>? returned,
        bool firstIsStored,
        PlatformReceiver? forValues)
    {
        // A callee that gets a sequence enumerates it. The step for the row has that.
        Followed? all =
            forValues != null
                ? null
                : operands
                    .Where(static operand => SodaFlowSymbols.IsSequence(operand.Parameter))
                    .Aggregate(
                        seed: (Followed?)null,
                        func: (followed, operand) =>
                            Both(first: followed, second: this.FollowEnumeration(operand.Written)));

        if (forValues == null)
        {
            all = Both(
                first: all,
                second: this.FollowKeptComparer(method: method, operands: operands, keeper: keeper));
        }

        PlatformCallbacks callbacks =
            (forValues == null ? calls : PlatformCallbacks.Equals) & this.region.Symbols.DeclaredCallbacks;

        // A callee that gets a comparer calls the comparer. It does not call CompareTo, Equals, or
        // GetHashCode of an object. A factory of a dictionary gets the comparer of its values
        // after the comparer of its keys.
        if (operands.Count(operand => IsComparison(operand.Parameter) && !this.IsNoValue(operand.Written))
            > (forValues == null ? 0 : 1))
        {
            callbacks &= PlatformCallbacks.Text;
        }

        // A dictionary that the code made with a comparer for its values calls that comparer.
        if (forValues != null && keeper != null && this.GotAValueComparer(keeper))
        {
            callbacks &= PlatformCallbacks.Text;
        }

        // A callee that compares the elements of one sequence with each other compares nothing
        // when the sequence has one element. It can make a hash code or text from that element.
        // A factory that gets its elements one by one, as Create does, is the same with one value.
        if ((callbacks & ComparesTwo) != PlatformCallbacks.None && PlatformCallers.ComparesOnlyItsElements(method))
        {
            ExpressionSyntax[] sequences =
            [
                .. operands
                    .Where(static operand => operand.IsReceiver || SodaFlowSymbols.IsSequence(operand.Parameter))
                    .Select(static operand => operand.Written)
            ];

            if (sequences.Length == 0 ? operands.Count == 1 : sequences.Length == 1 && this.HasOneElement(sequences[0]))
            {
                callbacks &= ~ComparesTwo;
            }
        }

        if (callbacks == PlatformCallbacks.None)
        {
            return all;
        }

        ImmutableArray<ITypeSymbol> compared =
            PlatformCallers.ComparedTypes(method: method, ofValues: forValues != null);

        // A type can be the type of two type parameters, as in a queue whose elements and priorities
        // have one type. The type parameter then says which values the method compares.
        ImmutableHashSet<ITypeParameterSymbol>? comparedParameters =
            PlatformCallers.ComparedTypeParameters(method: method, ofValues: forValues != null);

        INamedTypeSymbol collection = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;

        // A callee that gets a function compares what the function returns, as a sort by a key
        // does. It does not compare the values that it gives to the function.
        // For the values of a dictionary, a function for the key says nothing. The method
        // compares the elements of its source when no function makes the values.
        bool byFunction =
            !PlatformCallers.ComparesWhatAFunctionChooses(method)
            && operands.Any(static operand => operand.Parameter?.TypeKind == TypeKind.Delegate)
            && !(forValues != null && PlatformCallers.MakesItsValuesFromItsElements(method));

        // Some of those methods also compare the values of a sequence that they get, as ExceptBy
        // compares the keys of its second sequence.
        bool alsoKeys = byFunction && PlatformCallers.ComparesKeysBesideAFunction(method);

        // A member of LINQ to XML knows its content as object, and makes text from each object of
        // a sequence in it.
        bool takesContent = SodaFlowSymbols.TakesXmlContent((method.ReducedFrom ?? method).OriginalDefinition);
        List<(ExpressionSyntax Written, LoopState State, Construction Members, bool Gets)> owners = [];

        // For some methods, each runtime gives the call to the same one of the two objects.
        bool hasCollection = operands.Any(static operand => operand.IsReceiver);

        PlatformReceiver receiver =
            !hasCollection && !firstIsStored
                ? PlatformReceiver.Either
                : forValues ?? this.ReceiverFor(method: method, operands: operands, keeper: keeper);

        // The object that gets the call can get a second object of its own side as the argument.
        bool among = receiver is PlatformReceiver.StoredAlsoAmong or PlatformReceiver.GivenAlsoAmong;

        int firstValue = operands.TakeWhile(static operand => operand.IsReceiver).Count();
        LoopState argument = LoopState.None;

        for (int position = 0; position < operands.Count; position++)
        {
            Operand operand = operands[position];

            // The element of Enqueue gets no call, also when it has the type of the priority. The
            // elements of the first sequence of ExceptBy get none: the method compares their keys.
            if (comparedParameters != null
                && (operand.Parameter as ITypeParameterSymbol
                    ?? SodaFlowSymbols.ElementTypeOf(operand.Parameter) as ITypeParameterSymbol) is { } own
                && !comparedParameters.Contains(own))
            {
                continue;
            }

            bool isStored = hasCollection ? operand.IsReceiver : position == 0;

            bool gets =
                receiver switch
                {
                    PlatformReceiver.Stored or PlatformReceiver.StoredAlsoAmong => isStored,
                    PlatformReceiver.Given or PlatformReceiver.GivenAlsoAmong => !isStored,
                    PlatformReceiver.FirstValue => position == firstValue,
                    _ => true
                };

            // The parts of a pair that the method compares. The key of a pair has the type parameter
            // for the key.
            IReadOnlyList<ITypeSymbol>? partTypes =
                comparedParameters == null
                    ? null
                    : operand.IsReceiver
                        ? PartTypesOf(SodaFlowSymbols.ElementTypeOf(collection))
                          ?? (collection.TypeParameters.Length > 1 ? collection.TypeParameters : null)
                        : PartTypesOf(operand.Parameter);

            Func<int, bool>? keeps =
                partTypes == null || comparedParameters == null
                    ? null
                    : part => part < 0
                              || part >= partTypes.Count
                              || partTypes[part] is not ITypeParameterSymbol parameter
                              || comparedParameters.Contains(parameter);

            if (!gets || among)
            {
                argument = argument.Join(
                    KeptParts(
                        state: this.Eval(operand.Written),
                        written: this.TypeBeforeObject(operand.Written),
                        keeps: keeps));
            }

            if (byFunction)
            {
                if (given != null && operand.Written is AnonymousFunctionExpressionSyntax lambda)
                {
                    LoopState parameters = given();

                    LoopState result =
                        this.EvalLambdaResult(
                            lambda: lambda,
                            boundParameters: ParametersOf(lambda).Select(_ => parameters).ToImmutableArray());

                    owners.AddRange(
                        CallbackOwners(state: result, written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members, true)));
                }
                else if (returned != null && operand.Parameter?.TypeKind == TypeKind.Delegate)
                {
                    // A method, a local function, or a lambda in a local. The evaluator follows it
                    // as the call of it, and that result has the state of what it returns.
                    owners.AddRange(
                        CallbackOwners(state: returned(), written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members, true)));
                }

                if (!alsoKeys)
                {
                    continue;
                }
            }

            // A method that makes text from its arguments does not read its receiver, which has
            // the text so far.
            if (callbacks == PlatformCallbacks.Text && operand.IsReceiver && operands.Count > 1)
            {
                continue;
            }

            owners.AddRange(
                CallbackOwners(
                        state: this.Eval(operand.Written),
                        written: this.TypeBeforeObject(operand.Written),
                        asSequence: operand.IsReceiver
                                    || SodaFlowSymbols.IsSequence(operand.Parameter)
                                    || (takesContent
                                        && SodaFlowSymbols.IsSequence(this.region.TypeOf(operand.Written))),
                        compared: compared,
                        keeps: keeps)
                    .Select(owner => (operand.Written, owner.State, owner.Members, gets)));
        }

        bool knowsTheReceiver = receiver != PlatformReceiver.Either;

        // The object that gets no call is the argument of the member. The object that the method
        // compares can be a part of a value, as the priority of a pair in a queue is.
        if (knowsTheReceiver)
        {
            argument = owners
                .Where(static owner => !owner.Gets)
                .Aggregate(seed: argument, func: static (joined, owner) => joined.Join(owner.State));
        }

        // The object that gets the call can be one that the evaluator did not see made. An element
        // of a list from a different place is such an object. It has the type of the other
        // object, and no state. The empty collection of the platform has no such object.
        if (knowsTheReceiver
            && !owners.Any(static owner => owner.Gets)
            && !operands.Any(operand => operand.IsReceiver && this.IsTheEmptyCollection(operand.Written)))
        {
            owners.AddRange(
                owners.ToArray()
                    .Select(static owner =>
                        (owner.Written, LoopState.None.WithMembers(owner.Members.WithNoState),
                            owner.Members.WithNoState, true)));
        }

        // An object with no member that has a state reads nothing through its own members. Its
        // member can read a second object that it gets. When the analyzer does not know which
        // object gets the call, the walk of that second object reports the read. A field can give
        // an object a state that is not in its members.
        if (!this.region.HasFields)
        {
            owners.RemoveAll(owner =>
                !owner.Members.HasState && !(knowsTheReceiver && owner.Gets && !argument.IsNone));
        }

        if (owners.Count == 0)
        {
            return all;
        }

        // The platform gives one object to a member of a second one, as a sort does to CompareTo.
        // When the analyzer does not know which of them gets the call, each object can be the
        // parameter.
        LoopState others =
            (knowsTheReceiver
                ? argument
                : owners.Aggregate(seed: also, func: static (joined, owner) => joined.Join(owner.State)))
            .WithNoObjects;

        return owners.Aggregate(
            seed: all,
            func: (followed, owner) =>
            {
                // A member with a parameter gets one more of the objects.
                (ExpressionSyntax Written, LoopState State)[] one = [(owner.Written, others)];

                // An object that is only the argument gets no call with two objects. The method
                // can make its hash code or its text.
                PlatformCallbacks its = owner.Gets ? callbacks : callbacks & ~ComparesTwo;

                return this.CallbackMembers(members: owner.Members, callbacks: its)
                    .Aggregate(
                        seed: followed,
                        func: (found, member) =>
                            Both(
                                first: found,
                                second: this.FollowImplied(
                                    call: owner.Written,
                                    member: member,
                                    owner: owner.State,
                                    arguments: member.Parameters.Length == 1 ? one : [])));
            });
    }

    /// <summary>
    ///     Which object gets the call from a method of the platform. A set
    ///     operation can use a different path in the platform when its argument is
    ///     a set with the same comparer.
    /// </summary>
    /// <remarks>
    ///     The platform uses that path only when the two comparers are equal. The
    ///     analyzer knows that when it sees that each set was made with the default
    ///     comparer. When it sees that only one of them got a different comparer,
    ///     the method has its row. In each other case, the analyzer reads the two
    ///     objects. A collection expression for a sequence makes a list or an
    ///     array, which is not a set. An argument whose type the analyzer does not
    ///     know can be such a set, and then the analyzer reads the two objects
    ///     also.
    /// </remarks>
    private PlatformReceiver ReceiverFor(
        IMethodSymbol method,
        IEnumerable<Operand> operands,
        ExpressionSyntax? keeper)
    {
        PlatformReceiver row = PlatformCallers.ReceiverOf(method);

        if (!PlatformCallers.DependsOnItsArgument(method)
            || operands.Where(static operand => !operand.IsReceiver && SodaFlowSymbols.IsSequence(operand.Parameter))
                .Select(static operand => operand.Written)
                .FirstOrDefault() is not { } argument
            || (Unwrapped(argument) is CollectionExpressionSyntax
                && this.region.TypeOf(argument) is { TypeKind: TypeKind.Interface }))
        {
            return row;
        }

        if (this.KnownTypeOf(argument) is not { } type)
        {
            return PlatformReceiver.Either;
        }

        if (PlatformCallers.ReceiverForASet(method: method, argument: type) is not { } forASet || forASet == row)
        {
            return row;
        }

        return (keeper == null ? KeptComparer.NotKnown : this.ComparerOf(keeper), this.ComparerOf(argument)) switch
        {
            (KeptComparer.Default, KeptComparer.Default) => forASet,
            (KeptComparer.Default, KeptComparer.Different) or (KeptComparer.Different, KeptComparer.Default) => row,
            _ => PlatformReceiver.Either
        };
    }

    // What the analyzer knows about the comparer that a set of the platform keeps.
    private enum KeptComparer
    {
        // The code made the set with no comparer, with null, or with the Default of the platform.
        Default,

        // The code made the set with a new comparer, or with a static member that is not Default.
        Different,
        NotKnown
    }

    // True when the code made the dictionary with a comparer for its values: a factory or
    // WithComparers that gets a second comparer. A member of a dictionary that gives a new
    // dictionary keeps its comparers. A local that no code changes has the dictionary that its
    // declaration made.
    private bool GotAValueComparer(ExpressionSyntax written)
    {
        for (int depth = 0; depth <= MaxKnownTypeDepth; depth++)
        {
            switch (Unwrapped(written))
            {
                case IdentifierNameSyntax name
                    when this.region.SymbolOf(name) is ILocalSymbol local
                         && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                             is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                         && NamesOf(local: local, declarator: declarator, region: this.region) is { } uses
                         && uses.All(static use => !IsWritten(use)):
                    written = made;

                    continue;
                case InvocationExpressionSyntax call
                    when this.region.SymbolOf(call) is IMethodSymbol maker
                         && SodaFlowSymbols.IsPlatform(maker.ContainingType):
                    if ((maker.ReducedFrom ?? maker).Parameters
                        .Where(static parameter => SodaFlowSymbols.IsComparer(parameter.Type))
                        .Skip(1)
                        .FirstOrDefault() is { } forTheValues)
                    {
                        return this.ComparerIn(
                                   arguments: call.ArgumentList.Arguments,
                                   maker: maker,
                                   parameter: forTheValues)
                               != KeptComparer.Default;
                    }

                    if (maker.IsStatic || call.Expression is not MemberAccessExpressionSyntax onADictionary)
                    {
                        return false;
                    }

                    written = onADictionary.Expression;

                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    // The comparer of a set that the code made. A creation, a collection expression, and a factory
    // of the platform say it. A local that no code changes has the set that its declaration made.
    private KeptComparer ComparerOf(ExpressionSyntax written)
    {
        for (int depth = 0; depth <= MaxKnownTypeDepth; depth++)
        {
            ExpressionSyntax inner = Unwrapped(written);

            switch (inner)
            {
                case IdentifierNameSyntax name
                    when this.region.SymbolOf(name) is ILocalSymbol local
                         && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                             is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                         && NamesOf(local: local, declarator: declarator, region: this.region) is { } uses
                         && uses.All(static use => !IsWritten(use)):
                    written = made;

                    continue;
                case CollectionExpressionSyntax:
                    return KeptComparer.Default;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments =
                inner switch
                {
                    ObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments ?? default,
                    ImplicitObjectCreationExpressionSyntax creation => creation.ArgumentList.Arguments,
                    InvocationExpressionSyntax call => call.ArgumentList.Arguments,
                    _ => default
                };

            if (inner is not (BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax)
                || this.region.SymbolOf(inner) is not IMethodSymbol maker
                || !SodaFlowSymbols.IsPlatform(maker.ContainingType)
                || (maker.MethodKind != MethodKind.Constructor && !(maker.ReducedFrom ?? maker).IsStatic))
            {
                return KeptComparer.NotKnown;
            }

            return (maker.ReducedFrom ?? maker).Parameters
                .Where(static parameter => SodaFlowSymbols.IsComparer(parameter.Type))
                .Select(parameter => this.ComparerIn(arguments: arguments, maker: maker, parameter: parameter))
                .DefaultIfEmpty(KeptComparer.Default)
                .First();
        }

        return KeptComparer.NotKnown;
    }

    // The comparer that a call gives for a parameter. A call that gives none, null, default, or
    // the Default of the platform gives the default comparer.
    private KeptComparer ComparerIn(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol maker,
        IParameterSymbol parameter)
    {
        // The object of an extension method is not in the list of its arguments.
        int position = parameter.Ordinal - (maker.ReducedFrom == null ? 0 : 1);

        ExpressionSyntax? given =
            arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == parameter.Name)
                ?.Expression
            ?? (position >= 0 && position < arguments.Count && arguments[position].NameColon == null
                ? arguments[position].Expression
                : null);

        if (given == null || this.IsNoValue(given))
        {
            return KeptComparer.Default;
        }

        return this.region.SymbolOf(Unwrapped(given)) switch
        {
            IPropertySymbol { IsStatic: true, Name: "Default" } property
                when SodaFlowSymbols.IsPlatform(property.ContainingType)
                     && property.ContainingType.Name is "EqualityComparer" or "Comparer" => KeptComparer.Default,
            IMethodSymbol { MethodKind: MethodKind.Constructor } => KeptComparer.Different,
            IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true } => KeptComparer.Different,
            _ => KeptComparer.NotKnown
        };
    }

    /// <summary>
    ///     The calls that a method of the platform makes on a comparer of this
    ///     compilation which its collection keeps. The constructor of a sorted set
    ///     keeps the comparer that it gets, and calls nothing. Then, each method
    ///     of the set that compares calls the comparer. A constructor that gets
    ///     items and a comparer calls the comparer itself.
    /// </summary>
    /// <param name="method">The method of the platform.</param>
    /// <param name="operands">The values that the method gets.</param>
    /// <param name="keeper">The collection that the code calls the method on. Null when there is none.</param>
    private Followed? FollowKeptComparer(
        IMethodSymbol method,
        IReadOnlyCollection<Operand> operands,
        ExpressionSyntax? keeper)
    {
        const PlatformCallbacks comparing =
            PlatformCallbacks.Compare | PlatformCallbacks.Equals | PlatformCallbacks.Hash;

        if ((PlatformCallers.Of(method) & comparing) == PlatformCallbacks.None)
        {
            return null;
        }

        // The collection has the state of the comparer that its constructor got.
        IEnumerable<ExpressionSyntax> holders =
            method.MethodKind == MethodKind.Constructor
                ? operands.Any(static operand => SodaFlowSymbols.IsSequence(operand.Parameter))
                    ? operands
                        .Where(static operand => SodaFlowSymbols.IsComparer(operand.Parameter))
                        .Select(static operand => operand.Written)
                    : []
                : keeper != null
                    ? [keeper]
                    : [];

        Followed? all = null;

        foreach (ExpressionSyntax holder in holders)
        {
            if (this.Eval(holder) is not { Members: { HasType: true } members } kept)
            {
                continue;
            }

            all =
                members.Implementations(SodaFlowSymbols.IsComparerInterface)
                    .Aggregate(
                        seed: all,
                        func: (found, member) =>
                            Both(
                                first: found,
                                second: this.FollowImplied(
                                    call: holder,
                                    member: member,
                                    owner: kept,
                                    arguments: [])));
        }

        return all;
    }

    /// <summary>
    ///     The objects of this compilation in a value that a method of the
    ///     platform gets, when the method can call a member of them. The value is
    ///     such an object, a sequence of them, or a tuple that has one.
    /// </summary>
    /// <param name="state">The state of the value.</param>
    /// <param name="written">The type of the expression for the value. Null when it is not known.</param>
    /// <param name="asSequence">True when the method reads the elements of a sequence that it gets here.</param>
    /// <param name="compared">The types of the values that the method compares. Empty for each type.</param>
    /// <param name="keeps">
    ///     For a pair, true for the position of each part that the method
    ///     compares. Null when the method compares each part that has a type of
    ///     <paramref name="compared" />.
    /// </param>
    private static IEnumerable<(LoopState State, Construction Members)> CallbackOwners(
        LoopState state,
        ITypeSymbol? written,
        bool asSequence,
        ImmutableArray<ITypeSymbol> compared,
        Func<int, bool>? keeps = null)
    {
        if (state.Members is not { } members)
        {
            return [];
        }

        // The state of a tuple, of a Tuple object, or of a KeyValuePair has the state of each part.
        bool ofATuple = !members.HasType;

        if (ofATuple || (SodaFlowSymbols.HoldsParts(written) && members.HasParts))
        {
            return members.PartsAt(ofATuple)
                .Where(part => keeps?.Invoke(part.Position) ?? true)
                .SelectMany(part =>
                    CallbackOwners(state: part.State, written: null, asSequence: false, compared: compared));
        }

        // A list has the members of its elements in its state. The list is one of those objects
        // only for a method that reads elements. A type such as object does not tell a list from
        // an element, and the state then says which the value is.
        // The elements of a dictionary are pairs, and the method compares the keys in them. The
        // elements of a lookup are groups, and each group has its key.
        ITypeSymbol? element = asSequence ? SodaFlowSymbols.ElementTypeOf(written) : null;

        // A collection with no type argument, as an ArrayList is, does not say the type of its
        // elements. The object in its state is then one of them.
        bool isOwner =
            ((written == null || members.Is(written)) && !(state.IsContainer && Construction.SaysNothing(written)))
            || (element != null
                && (members.Is(element)
                    || (element is INamedTypeSymbol pair
                        && (SodaFlowSymbols.HoldsParts(pair) || SodaFlowSymbols.IsGrouping(pair))
                        && pair.TypeArguments.Any(members.Is))))
            || (asSequence && element == null && SodaFlowSymbols.IsSequence(written));

        return isOwner && (compared.IsEmpty || compared.Any(members.Is)) ? [(state, members)] : [];
    }

    // The state of a value without the parts of a pair that the method does not compare.
    private static LoopState KeptParts(LoopState state, ITypeSymbol? written, Func<int, bool>? keeps)
    {
        if (keeps == null
            || state.Members is not { } members
            || (members.HasType && !(SodaFlowSymbols.HoldsParts(written) && members.HasParts)))
        {
            return state;
        }

        return members.PartsAt(ofATuple: !members.HasType)
            .Where(part => keeps(part.Position))
            .Aggregate(seed: LoopState.None, func: static (joined, part) => joined.Join(part.State));
    }

    // The type of each part of a pair: a tuple, a KeyValuePair, or a Tuple object. For a sequence, the
    // parts of its element. Null for each other type.
    private static IReadOnlyList<ITypeSymbol>? PartTypesOf(ITypeSymbol? type) =>
        type switch
        {
            INamedTypeSymbol { IsTupleType: true } tuple => [.. tuple.TupleElements.Select(static part => part.Type)],
            INamedTypeSymbol { TypeArguments.IsEmpty: false } holder when SodaFlowSymbols.HoldsParts(holder) =>
                holder.TypeArguments,
            _ when SodaFlowSymbols.ElementTypeOf(type) is { } element => PartTypesOf(element),
            _ => null
        };

    private IEnumerable<IMethodSymbol> CallbackMembers(Construction members, PlatformCallbacks callbacks)
    {
        SodaFlowSymbols symbols = this.region.Symbols;

        if ((callbacks & PlatformCallbacks.Compare) != PlatformCallbacks.None)
        {
            foreach (IMethodSymbol compare in members.Implementations(SodaFlowSymbols.IsComparableInterface))
            {
                yield return compare;
            }
        }

        if ((callbacks & PlatformCallbacks.Equals) != PlatformCallbacks.None)
        {
            foreach (IMethodSymbol typed in members.Implementations(SodaFlowSymbols.IsEquatableInterface))
            {
                yield return typed;
            }

            if (symbols.ObjectEquals is { } general)
            {
                yield return general;
            }
        }

        if ((callbacks & PlatformCallbacks.Hash) != PlatformCallbacks.None && symbols.ObjectGetHashCode is { } hash)
        {
            yield return hash;
        }

        if ((callbacks & PlatformCallbacks.Text) != PlatformCallbacks.None && symbols.ObjectToString is { } text)
        {
            yield return text;
        }
    }

    // An argument that is null or default, or a local that only gets such a value. A callee that
    // gets it for a comparer has no comparer, and uses the default one, which calls the members of
    // the objects. A parameter is the same when the call gives it such a value, and the callee
    // does not change it.
    private bool IsNoValue(ExpressionSyntax written) =>
        IsNoValueLiteral(written)
        || (Unwrapped(written) is IdentifierNameSyntax name
            && this.region.SymbolOf(name) switch
            {
                ILocalSymbol local => this.OnlyGetsNoValue(local),
                IParameterSymbol parameter => this.GetsNoValue(parameter),
                _ => false
            });

    // A call that gives no argument for an optional parameter gives its default value. The walk of
    // a callee binds each argument that the call gives, thus a parameter with no binding got none.
    private bool GetsNoValue(IParameterSymbol parameter) =>
        (this.noValues.Contains(parameter)
         || (this.depth > 0
             && !this.bound.ContainsKey(parameter)
             && parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: null }))
        && !IsWrittenIn(parameter: parameter, region: this.region);

    private static bool IsNoValueLiteral(ExpressionSyntax written) =>
        Unwrapped(written)
            is LiteralExpressionSyntax
            {
                RawKind: (int)SyntaxKind.NullLiteralExpression or (int)SyntaxKind.DefaultLiteralExpression
            }
            or DefaultExpressionSyntax;

    // True for a local whose declaration and each assignment in its block give it null or default.
    // A ref or a deconstruction can give it a value that the code does not say here. The compound
    // assignment of a comparer is ??=, and with null or default it leaves the value as it was.
    private bool OnlyGetsNoValue(ISymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarator
        && (declarator.Initializer == null || IsNoValueLiteral(declarator.Initializer.Value))
        && NamesOf(local: local, declarator: declarator, region: this.region) is { } names
        && names.All(static name =>
            !IsWritten(name)
            || (name.Parent is AssignmentExpressionSyntax assignment
                && assignment.Left == name
                && IsNoValueLiteral(assignment.Right)));

    // A comparer, or a delegate of the type Comparison, which a sort can get.
    private static bool IsComparison(ITypeSymbol? type) =>
        SodaFlowSymbols.IsComparer(type)
        || type is INamedTypeSymbol { TypeKind: TypeKind.Delegate, Name: "Comparison" }
        // A type parameter for a comparer, as in Sort<T, TComparer>.
        || (type is ITypeParameterSymbol generic && generic.ConstraintTypes.Any(SodaFlowSymbols.IsComparer));

    // The elements that a callee gets from a sequence of this compilation, when the callee
    // enumerates what it gets for the parameter.
    private LoopState Enumerated(ExpressionSyntax? written, ITypeSymbol? parameter) =>
        written != null && SodaFlowSymbols.IsSequence(parameter)
            ? this.FollowEnumeration(written)?.State ?? LoopState.None
            : LoopState.None;

    /// <summary>
    ///     The call of a <c>Deconstruct</c> method. Each parameter gets an
    ///     argument, thus the result has the state that the method gives to each
    ///     <c>out</c> parameter.
    /// </summary>
    /// <param name="call">The deconstruction, or the part of a pattern.</param>
    /// <param name="method">The method that the compiler found.</param>
    /// <param name="value">The expression for the value, for a report.</param>
    /// <param name="owner">The state of the value.</param>
    public Followed? FollowDeconstruct(SyntaxNode call, IMethodSymbol method, ExpressionSyntax value, LoopState owner)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        // An extension method gets the value as its first argument.
        return this.FollowImplied(
            call: call,
            member: definition,
            owner: definition.IsStatic ? null : owner,
            arguments:
            [
                .. definition.Parameters.Select((_, position) =>
                    (Written: value, State: definition.IsStatic && position == 0 ? owner : LoopState.None))
            ]);
    }

    private Followed? FollowIndexer(SyntaxNode access, ExpressionSyntax owner, BaseArgumentListSyntax indexes) =>
        this.region.SymbolOf(access) is IPropertySymbol { GetMethod: { } getter }
            ? this.FollowImplied(
                call: access,
                member: getter,
                owner: this.Self(owner),
                arguments: [.. indexes.Arguments.Select(index => (index.Expression, this.Eval(index.Expression)))])
            : null;

    private Followed? FollowOperator(SyntaxNode call, ISymbol? operation, IEnumerable<ExpressionSyntax> operands) =>
        operation is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } method
            ? this.FollowImplied(
                call: call,
                member: method,
                owner: null,
                arguments: [.. operands.Select(operand => (operand, this.Eval(operand)))])
            : null;

    // The name of a member in a report: the property for an accessor, and the operator as the
    // code declares it.
    private static string ImpliedNameOf(IMethodSymbol definition) =>
        definition.AssociatedSymbol?.Name
        ?? (definition.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion
            ? definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : definition.Name);

    /// <summary>
    ///     The read of a property that a property pattern names, on a value with
    ///     the given state. A pattern has no expression for the owner of the
    ///     property, thus the walk gives the state of the owner.
    /// </summary>
    /// <param name="name">The name of the property in the pattern.</param>
    /// <param name="owner">The state of the value that the pattern is for.</param>
    public Followed? FollowPatternMember(ExpressionSyntax name, LoopState owner)
    {
        if (this.followed.TryGetValue(key: name, value: out Followed? known))
        {
            return known;
        }

        Followed? result =
            this.region.SymbolOf(name) is IPropertySymbol written && !this.region.Symbols.IsSodaFlow(written)
                ? this.FollowGetterOn(access: name, written: written, owner: owner)
                : null;

        this.followed[name] = result;

        return result;
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
    ///     that the <c>Lazy</c> has. The result is null when it has no function
    ///     that the evaluator can read.
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
            outs: followed.Outs);

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
                            each: (written, given))));
    }

    private bool ReturnsDelegate(FunctionValue function) =>
        (function.Declaration is AnonymousFunctionExpressionSyntax
            ? this.region.SymbolOf(function.Declaration)
            : this.region.ModelFor(function.Declaration).GetDeclaredSymbol(function.Declaration))
        is IMethodSymbol { ReturnType.TypeKind: TypeKind.Delegate };

    /// <summary>
    ///     Walks the body of a callee as a call from this code, and gives the
    ///     caller what the walk found.
    /// </summary>
    /// <param name="call">The call, which is where the caller reports a finding about a member.</param>
    /// <param name="name">The name of the callee, for a report.</param>
    /// <param name="body">The block or the expression of the callee.</param>
    /// <param name="bindings">The arguments, each with the parameter that gets it.</param>
    /// <param name="definer">
    ///     For a lambda or a local function, the evaluator of the code that
    ///     contains it. The body reads the locals of that code, and is part of its
    ///     frame. Null for a callee with a frame of its own.
    /// </param>
    /// <param name="members">The members of the object that the callee is a member of.</param>
    /// <param name="receiver">
    ///     The object, when the call names it and the members have that object as
    ///     their origin.
    /// </param>
    /// <param name="target">The delegate, when the call is a call of a delegate.</param>
    private Followed? Enter(
        SyntaxNode call,
        string name,
        SyntaxNode body,
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members,
        LoopState? receiver,
        LoopState? target)
    {
        // Nothing from the loop goes in, thus nothing from the loop comes out. A lambda or a local
        // function can read the locals of the code that it is in, thus the walk always reads it.
        // A callee that gets an object of the store can give the object a second name. The walk
        // reads such a callee while ReadsForObjects permits.
        bool noState =
            definer == null
            && members.Count == 0
            && !this.region.HasFields
            && bindings.All(static binding => binding.State.IsNone);

        switch (noState)
        {
            case true when !this.ReadsForObjects(bindings):
                return Followed.Nothing;
            case false when this.depth >= MaxInlineDepth || !this.region.TryEnter():
                this.DoesNotRead(call: call, name: name, members: members);

                return null;
        }

        // A function of the walk, read by an evaluator that changes nothing: it gets a copy of
        // the locals of its code also.
        if (this.quiet && definer != null)
        {
            definer = definer.Quiet();
        }

        // The body of a lambda or of a local function is a part of that function.
        LoopStateEvaluator inner =
            this.Inner(
                call: call,
                bindings: bindings,
                definer: definer,
                members: members,
                function: definer == null
                    ? null
                    : body.FirstAncestorOrSelf<SyntaxNode>(static node =>
                        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

        List<Finding> found = [];
        SinkWalker walker = new(region: this.region, evaluator: inner, sink: found.Add);
        walker.Visit(body);
        found.AddRange(inner.Frame.TakeUnread());

        // A lambda and a local function are part of a frame that can run more code. A query of
        // such a frame stays for the code that enumerates it. A callee that returns runs no more
        // code, and a function that runs after its frame returned is the same. The walk reads
        // the queries of such a frame now, and each query keeps what the walk found.
        IReadOnlyList<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving =
            definer == null || inner.Frame.HasReturned ? walker.RunWaitingApart() : [];

        return this.Leave(
            call: call,
            name: name,
            bindings: bindings,
            inner: inner,
            lexical: definer != null,
            receiver: receiver,
            target: target,
            result: body is ExpressionSyntax expression
                ? inner.Eval(expression)
                : inner.JoinAll(ReturnExpressions(body)),
            found: found,
            leaving: leaving);
    }

    /// <summary>
    ///     Records a member that this code calls and that the walk does not read.
    ///     It does that only when a member of the same object has a state, because
    ///     the code can read that member. A pending argument of such a call gets
    ///     the same report from the walk.
    /// </summary>
    private void DoesNotRead(SyntaxNode call, string name, Dictionary<ISymbol, LoopState> members)
    {
        if (this.quiet || members.Values.FirstOrDefault(static member => !member.IsNone) is not { } pending)
        {
            return;
        }

        this.Frame.DoesNotRead(
            new Finding(
                home: this.Frame,
                origin: pending.AnyOrigin,
                escape: true,
                diagnostic: Diagnostic.Create(
                    descriptor: Diagnostics.Escape,
                    location: call.GetLocation(),
                    this.region.PlaceholderName,
                    name),
                subject: name));
    }

    // True when the walk reads a callee that gets no state from the loop. It does that only for an
    // object of the store, to the depth limit, and while the region has frames for such callees.
    private bool ReadsForObjects(IEnumerable<Binding> bindings) =>
        bindings.Any(static binding => !binding.State.Objects.IsEmpty)
        && this.depth < MaxInlineDepth
        && this.region.TryEnterForObjects();

    /// <summary>
    ///     The evaluator for the body of a callee, with each parameter bound to
    ///     the state of its argument. The parameter is the origin of that state.
    /// </summary>
    private LoopStateEvaluator Inner(
        SyntaxNode call,
        IEnumerable<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members,
        SyntaxNode? function)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder parameters = (definer?.bound ?? NoSymbols).ToBuilder();
        ImmutableHashSet<ISymbol>.Builder noValue = (definer?.noValues ?? NoParameters).ToBuilder();
        HashSet<ISymbol> given = new(SymbolEqualityComparer.Default);
        HashSet<ISymbol> withAValue = new(SymbolEqualityComparer.Default);

        foreach (Binding binding in bindings)
        {
            LoopState state = binding.State.From(binding.Parameter);

            // A params parameter gets the states of all its arguments.
            parameters[binding.Parameter] =
                given.Add(binding.Parameter) ? state : parameters[binding.Parameter].Join(state);

            // A parameter has no value when each argument for it is null or default.
            if (this.IsNoValue(binding.Argument) && !withAValue.Contains(binding.Parameter))
            {
                noValue.Add(binding.Parameter);
            }
            else
            {
                withAValue.Add(binding.Parameter);
                noValue.Remove(binding.Parameter);
            }
        }

        return new LoopStateEvaluator(
            region: this.region,
            bound: parameters.ToImmutable(),
            noValues: noValue.ToImmutable(),
            depth: this.depth + 1,
            frame: definer?.Frame
                   ?? new LoopFrame(
                       parent: this.Frame,
                       repeats: !this.RunsOneTime(call),
                       isOfAConstructor: call is BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax),
            locals: definer?.locals ?? new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            assigned: definer?.assigned ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            thisMembers: definer?.thisMembers ?? members,
            quiet: this.quiet,
            function: function);
    }

    /// <summary>
    ///     Changes the result and the findings of a callee from the names of the
    ///     callee to the names of this code. A finding about a parameter becomes a
    ///     finding at the argument for that parameter. A finding about a member
    ///     becomes a finding at the call.
    /// </summary>
    /// <remarks>
    ///     A query that the callee made and did not enumerate keeps its findings,
    ///     changed as each other finding is. This code reports them where it
    ///     enumerates the query. The query is then one of the queries of the frame
    ///     of this code, which can give it to its own caller.
    /// </remarks>
    private Followed Leave(
        SyntaxNode call,
        string name,
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator inner,
        bool lexical,
        LoopState? receiver,
        LoopState? target,
        LoopState result,
        IEnumerable<Finding> found,
        IEnumerable<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving)
    {
        Dictionary<ISymbol, Binding> byParameter = new(SymbolEqualityComparer.Default);

        foreach (Binding binding in bindings)
        {
            if (!byParameter.TryGetValue(key: binding.Parameter, value: out Binding first) || first.State.IsNone)
            {
                byParameter[binding.Parameter] = binding;
            }
        }

        // A lambda that a callee runs is part of the frame whose code contains it. That frame can
        // be a different one from this code.
        bool sameFrame = ReferenceEquals(objA: inner.Frame, objB: this.Frame);

        // A function can run after the code that made it returned. A lambda that a constructor
        // kept in a field is such a function. No walk of that code gets the finding. Thus, this
        // code reports it at the call, as it does for a callee with a frame of its own.
        bool returned = lexical && !sameFrame && inner.Frame.HasReturned;

        if (!lexical)
        {
            inner.Frame.Return();
        }

        foreach ((QueryValue query, IReadOnlyList<Finding> foundInIt) in leaving)
        {
            query.Hold(Changed(foundInIt));

            if (!this.quiet)
            {
                this.Frame.Wait(query);
            }
        }

        ImmutableArray<Finding> findings = Changed(found);

        ISymbol? inputs =
            bindings.Select(static binding => binding.State.AnyOrigin).FirstOrDefault(static origin => origin != null)
            ?? receiver?.AnyOrigin
            ?? target?.Origin;

        ImmutableDictionary<ISymbol, LoopState>.Builder outs =
            ImmutableDictionary.CreateBuilder<ISymbol, LoopState>(SymbolEqualityComparer.Default);

        // A parameter has a state here only when the callee gave it one. For a parameter that is
        // not ref or out, that is a value that the callee put into the object, as Add does. The
        // caller has the same object. An assignment to the parameter changes only the local of
        // the callee, and a struct is a copy.
        foreach (Binding binding in bindings.Where(binding =>
                     binding.Parameter.RefKind != RefKind.None
                     || (!binding.Parameter.Type.IsValueType && !inner.assigned.Contains(binding.Parameter))))
        {
            if (inner.locals.TryGetValue(key: binding.Parameter, value: out LoopState? assigned))
            {
                outs[binding.Parameter] = assigned.WithOrigins(Out).FromIfUnknown(inputs);
            }
        }

        return new Followed(
            state: result.WithOrigins(Out).FromIfUnknown(inputs),
            findings: findings,
            outs: outs.ToImmutable());

        // The findings of the callee, in the names of this code.
        ImmutableArray<Finding> Changed(IEnumerable<Finding> from)
        {
            ImmutableArray<Finding>.Builder changed = ImmutableArray.CreateBuilder<Finding>();

            foreach (Finding finding in from)
            {
                // A finding of a different frame goes to that frame as it is.
                if (!ReferenceEquals(objA: finding.Home, objB: inner.Frame))
                {
                    changed.Add(finding);

                    continue;
                }

                if (finding.Origin != null && byParameter.TryGetValue(key: finding.Origin, value: out Binding binding))
                {
                    // An AsCell() at the argument is the assertion of the caller that nothing reads the
                    // value. The analyzer reports a read that it can see, whatever the caller asserted.
                    if (finding.Escape && IsAsserted(binding.Argument))
                    {
                        continue;
                    }

                    // The read can be of a part of the argument, as of a member of an object. Then,
                    // the origin is the origin of a part.
                    changed.Add(
                        new Finding(
                            home: this.Frame,
                            origin: binding.State.AnyOrigin,
                            escape: finding.Escape,
                            diagnostic: finding.Escape
                                ? Diagnostic.Create(
                                    descriptor: Diagnostics.Escape,
                                    location: binding.Argument.GetLocation(),
                                    this.region.PlaceholderName,
                                    finding.Subject)
                                : Diagnostic.Create(
                                    descriptor: Diagnostics.ReadInCallee,
                                    location: binding.Argument.GetLocation(),
                                    name,
                                    finding.Origin.Name,
                                    this.region.PlaceholderName),
                            subject: finding.Subject));

                    continue;
                }

                if (lexical && !returned)
                {
                    changed.Add(finding);

                    continue;
                }

                // The caller did not give a value that has no origin. A callee can only have it from a
                // placeholder type, and the analyzer does not know which loop that placeholder is of.
                if (finding.Origin == null)
                {
                    continue;
                }

                changed.Add(
                    new Finding(
                        home: this.Frame,
                        origin: Out(finding.Origin),
                        escape: finding.Escape,
                        diagnostic: finding.Escape
                            ? Diagnostic.Create(
                                descriptor: Diagnostics.Escape,
                                location: call.GetLocation(),
                                this.region.PlaceholderName,
                                finding.Subject)
                            : Diagnostic.Create(
                                descriptor: Diagnostics.MemberReadInCallee,
                                location: call.GetLocation(),
                                name,
                                finding.Origin.Name,
                                this.region.PlaceholderName),
                        subject: finding.Subject));
            }

            return changed.ToImmutable();
        }

        // The origin in this code of a value that has the given origin in the callee.
        ISymbol? Out(ISymbol? origin)
        {
            if (origin != null && byParameter.TryGetValue(key: origin, value: out Binding given))
            {
                return given.State.AnyOrigin;
            }

            if (lexical)
            {
                return sameFrame ? origin : target?.Origin;
            }

            return origin != null && receiver?.Members?.StateOf(origin) is { IsNone: false } member
                ? member.Origin ?? receiver.Origin
                : origin;
        }
    }

    /// <summary>
    ///     The members of the object that a method or a getter is a member of. A
    ///     call with no owner, or on <c>this</c>, is on the object that this code
    ///     is in. A call on a different object gets the members of that object, and
    ///     each member is the origin of its state.
    /// </summary>
    private (Dictionary<ISymbol, LoopState> Members, LoopState? Receiver) MembersFor(
        bool isStatic,
        ExpressionSyntax? owner)
    {
        if (isStatic)
        {
            return (new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default), null);
        }

        if (owner is null or ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return (this.thisMembers, null);
        }

        LoopState state = this.Eval(owner);

        return (TaggedMembers(state), state);
    }

    private static Dictionary<ISymbol, LoopState> TaggedMembers(LoopState owner)
    {
        Dictionary<ISymbol, LoopState> members = new(SymbolEqualityComparer.Default);

        if (owner.Members != null)
        {
            foreach (KeyValuePair<ISymbol, LoopState> pair in owner.Members.Members)
            {
                members[pair.Key] = pair.Value.From(pair.Key);
            }
        }

        return members;
    }

    /// <summary>
    ///     The arguments of a call, each with the parameter of the definition
    ///     that gets it, by name or by position as the compiler binds them. The
    ///     receiver of an extension method is the argument of its first parameter.
    /// </summary>
    private IReadOnlyList<Binding> Bind(
        IMethodSymbol definition,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ExpressionSyntax? extensionReceiver)
    {
        List<Binding> bindings = [];
        int offset = 0;

        if (extensionReceiver != null && definition.Parameters.Length > 0)
        {
            bindings.Add(
                new Binding(
                    parameter: definition.Parameters[0],
                    argument: extensionReceiver,
                    state: this.Eval(extensionReceiver)));

            offset = 1;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                is { } parameter)
            {
                bindings.Add(
                    new Binding(
                        parameter: parameter,
                        argument: arguments[i].Expression,
                        state: this.Eval(arguments[i].Expression)));
            }
        }

        return bindings;
    }

    private IReadOnlyList<Binding> BindByPosition(
        ImmutableArray<IParameterSymbol> parameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        List<Binding> bindings = [];

        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            bindings.Add(
                new Binding(
                    parameter: parameters[i],
                    argument: arguments[i].Expression,
                    state: this.Eval(arguments[i].Expression)));
        }

        return bindings;
    }

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

        return symbols.IsCarrierType(returnType) ? inputs : LoopState.None;
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
                this.Declare(local: local, state: this.Eval(declarator.Initializer?.Value));
            }
        }

        return this.JoinAll(ReturnExpressions(block));
    }

    /// <summary>
    ///     True when a function given to an operator reads a pending value. The
    ///     evaluator reads the function as a call of it, with each parameter bound
    ///     to the given state. This evaluator must be a quiet one, because the
    ///     function does not run now.
    /// </summary>
    private bool FunctionReadsPending(InvocationExpressionSyntax invocation, IMethodSymbol method, LoopState given)
    {
        return invocation.ArgumentList.Arguments
            .Select((argument, position) =>
                (Written: argument.Expression,
                    Parameter: ParameterFor(method: method, argument: argument, position: position)))
            // A lambda with no call and no property that reads cannot read a value.
            .Where(static argument =>
                argument.Written is not AnonymousFunctionExpressionSyntax lambda || CanRead(lambda.Body))
            .Any(Reads);

        bool Reads((ExpressionSyntax Written, IParameterSymbol? Parameter) argument)
        {
            LoopState functions = this.Eval(argument.Written);

            return Callables(state: functions, parameter: argument.Parameter)
                .Any(function =>
                    this.FollowFunction(
                            call: invocation,
                            name: NameOf(argument.Written),
                            function: function,
                            target: functions,
                            arguments: default,
                            each: (argument.Written, given))
                        is { } followed
                    && followed.Findings.Any(static finding => !finding.Escape));
        }
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
                            || (SodaFlowSymbols.IsComparer(type) && !this.Eval(written).IsNone))))
                {
                    return true;
                }

                continue;
            }

            if (!CanRead(lambda.Body))
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

    private static bool CanRead(SyntaxNode body) =>
        body.DescendantNodesAndSelf()
            .Any(static node =>
                node is InvocationExpressionSyntax
                    or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" or "CanExecute" }
                    or MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" or "CanExecute" });

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

    /// <summary>
    ///     The state of what a lambda given to an operator returns, for the
    ///     content of a cell of cells or a cell of streams.
    /// </summary>
    private LoopState LambdaContent(InvocationExpressionSyntax invocation, LoopState parameterState)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        return lambda == null
            ? LoopState.None
            : this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray.Create(parameterState));
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
                    quiet: this.quiet,
                    function: this.function);

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
    ///     close.
    /// </summary>
    public static bool IsAsserted(ExpressionSyntax argument)
    {
        ExpressionSyntax current = argument;

        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
        {
            if (access.Name.Identifier.ValueText is "AsCell" or "AsBehavior")
            {
                return true;
            }

            current = access.Expression;
        }

        return false;
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
}
