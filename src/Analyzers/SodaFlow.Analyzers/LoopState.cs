using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>What the analyzer knows about one expression in a loop region.</summary>
/// <remarks>
///     A class and not a struct. A state holds the state of the values it
///     contains: a cell of cells, a cell of streams, or a hold of an object.
/// </remarks>
internal sealed class LoopState
{
    private static readonly ImmutableArray<MadeObject> NoObjects = ImmutableArray<MadeObject>.Empty;

    public static readonly LoopState None = new(
        pending: false,
        linked: false,
        members: null,
        content: null,
        origin: null,
        functions: ImmutableArray<FunctionValue>.Empty,
        queries: ImmutableArray<QueryValue>.Empty,
        isContainer: false,
        objects: NoObjects,
        held: NoObjects);

    public static readonly LoopState PendingPlaceholder = new(
        pending: true,
        linked: true,
        members: null,
        content: null,
        origin: null,
        functions: ImmutableArray<FunctionValue>.Empty,
        queries: ImmutableArray<QueryValue>.Empty,
        isContainer: false,
        objects: NoObjects,
        held: NoObjects);

    public static readonly LoopState StreamPlaceholder = new(
        pending: false,
        linked: true,
        members: null,
        content: null,
        origin: null,
        functions: ImmutableArray<FunctionValue>.Empty,
        queries: ImmutableArray<QueryValue>.Empty,
        isContainer: false,
        objects: NoObjects,
        held: NoObjects);

    private readonly LoopState? content;

    public LoopState(bool pending, bool linked, bool contentPending, bool contentLinked)
        : this(
            pending: pending,
            linked: linked,
            members: null,
            content: contentPending || contentLinked
                ? new LoopState(
                    pending: contentPending,
                    linked: contentLinked,
                    members: null,
                    content: null,
                    origin: null,
                    functions: ImmutableArray<FunctionValue>.Empty,
                    queries: ImmutableArray<QueryValue>.Empty,
                    isContainer: false,
                    objects: NoObjects,
                    held: NoObjects)
                : null,
            origin: null,
            functions: ImmutableArray<FunctionValue>.Empty,
            queries: ImmutableArray<QueryValue>.Empty,
            isContainer: false,
            objects: NoObjects,
            held: NoObjects)
    {
    }

    private LoopState(
        bool pending,
        bool linked,
        Construction? members,
        LoopState? content,
        ISymbol? origin,
        ImmutableArray<FunctionValue> functions,
        ImmutableArray<QueryValue> queries,
        bool isContainer,
        ImmutableArray<MadeObject> objects,
        ImmutableArray<MadeObject> held)
    {
        this.Objects = objects;
        this.Held = held;
        this.Pending = pending;
        this.Linked = linked;
        this.Members = members;
        this.content = content;
        this.Origin = origin;
        this.Functions = functions;
        this.Queries = queries;
        this.IsContainer = isContainer && members != null;
    }

    /// <summary>
    ///     The initial value of the expression reads the placeholder's current
    ///     value. A read of it before the close throws.
    /// </summary>
    public bool Pending { get; }

    /// <summary>
    ///     The graph node of the expression is reachable from the placeholder
    ///     through links. A close with such a value is a dependency cycle.
    /// </summary>
    public bool Linked { get; }

    /// <summary>
    ///     The members of the object this expression names, when its construction
    ///     was followed. Null for a value with no members of its own.
    /// </summary>
    public Construction? Members { get; }

    /// <summary>
    ///     The name through which the value came into the code that the analyzer
    ///     reads now. It is a parameter of that code, or a member of its object.
    ///     Null in the block itself. The caller reports a read in a callee at the
    ///     call, and the origin says which argument of the call the read is about.
    /// </summary>
    public ISymbol? Origin { get; }

    /// <summary>
    ///     The functions that the expression can be, when it is a delegate: a
    ///     lambda, a local function, or a method of this compilation. A call of
    ///     the delegate runs them.
    /// </summary>
    public ImmutableArray<FunctionValue> Functions { get; }

    /// <summary>
    ///     The queries that the expression can be, or can contain. Each one runs
    ///     code of this compilation when something enumerates it, and not when the
    ///     code makes it.
    /// </summary>
    public ImmutableArray<QueryValue> Queries { get; }

    /// <summary>
    ///     True when the value contains the objects that have the members, and is
    ///     not one of them. A list, an array, and a query are such values. The
    ///     type of an expression usually tells a list from one of its elements,
    ///     but the type <c>object</c> does not.
    /// </summary>
    public bool IsContainer { get; }

    /// <summary>
    ///     The origin of the value, or of one part of it that has an origin. A
    ///     report about a read of a part of an argument uses it.
    /// </summary>
    public ISymbol? AnyOrigin => this.Origin ?? this.content?.AnyOrigin ?? this.Members?.AnyOrigin;

    /// <summary>
    ///     The objects of <see cref="ObjectStore" /> that the value can be. Empty
    ///     for a value that the walk did not see made, and for a struct, which is
    ///     a copy at each assignment.
    /// </summary>
    public ImmutableArray<MadeObject> Objects { get; }

    /// <summary>
    ///     For a collection, the objects of <see cref="ObjectStore" /> that it
    ///     holds. An element of the collection is one of them.
    /// </summary>
    private ImmutableArray<MadeObject> Held { get; }

    /// <summary>
    ///     The same state, for a value that is no object of the store and holds
    ///     none. A tuple and a pair are such values: their parts keep the objects.
    /// </summary>
    public LoopState WithNoObjects =>
        this.Objects.IsEmpty && this.Held.IsEmpty
            ? this
            : new LoopState(
                pending: this.Pending,
                linked: this.Linked,
                members: this.Members,
                content: this.content,
                origin: this.Origin,
                functions: this.Functions,
                queries: this.Queries,
                isContainer: this.IsContainer,
                objects: NoObjects,
                held: NoObjects);

    /// <summary>
    ///     A number for what this state says. Two states that say the same have
    ///     the same number. The walk of a loop reads the loop again while a read
    ///     changes this number for a location.
    /// </summary>
    public int Shape
    {
        get
        {
            unchecked
            {
                int shape = (this.Pending ? 1 : 0) | (this.Linked ? 2 : 0) | (this.IsContainer ? 4 : 0);

                shape = (shape * 397) ^ (this.content?.Shape ?? 0);
                shape = (shape * 397) ^ (this.Members?.Shape ?? 0);

                // Sum of the platform throws when a sum is too large for its type. These sums must not.
                // Each read of a loop adds the object of its turn to a location that is older than
                // the turn. Those objects have one number, and the sum has each number one time.
                shape = this.Objects.Select(static made => made.Shape)
                    .Distinct()
                    .Aggregate(seed: shape, func: static (sum, made) => sum + made * 31);

                shape = this.Held.Select(static made => made.Shape)
                    .Distinct()
                    .Aggregate(seed: shape, func: static (sum, made) => sum + made * 37);

                shape = this.Functions.Aggregate(
                    seed: shape,
                    func: static (sum, function) => sum + function.Declaration.SpanStart * 41);

                return shape + this.Queries.Length * 43;
            }
        }
    }

    /// <summary>
    ///     True for a state with members that holds no object. For a collection,
    ///     the members are then of a value that no object of the store is.
    /// </summary>
    public bool HasMembersOfNoObject => this.Members != null && this.Held.IsEmpty;

    /// <summary>
    ///     True for a value with members that is no object of the store. Only a
    ///     copy of this state has those members.
    /// </summary>
    public bool IsOfNoObject => this.Members != null && this.Objects.IsEmpty;

    /// <summary>True for a value that holds an object of the store, as a collection does.</summary>
    public bool HoldsAnObject => !this.Held.IsEmpty;

    /// <summary>
    ///     The one object that a collection holds, of those that an element of
    ///     the given type can be. Null when it holds two or more. Null also when
    ///     the one is no new object of the store, or is an object of a turn
    ///     before. A collection holds the objects of a collection that went in
    ///     also, and those are not its elements.
    /// </summary>
    public MadeObject? TheOneNewObjectOf(ITypeSymbol? element)
    {
        ImmutableArray<MadeObject> ofThatType = this.Held.RemoveAll(made => !made.CanBe(element));
        MadeObject? only = ofThatType.Length == 1 ? ofThatType[0] : null;

        return only is { IsNew: true, IsOfTurnsBefore: false } ? only : null;
    }

    /// <summary>
    ///     The same state, for a collection that got a value. It holds each object
    ///     that the value can be. When the value is a collection whose elements
    ///     went in, it holds what that collection holds.
    /// </summary>
    public LoopState HoldingWhatCameWith(LoopState value) =>
        this.Holding(Union(first: value.Objects, second: value.Held));

    /// <summary>
    ///     The same state, for a collection that also holds an object that the
    ///     store does not know.
    /// </summary>
    public LoopState HoldingAnUnknownObject => this.Holding(ImmutableArray.Create(MadeObject.Unknown));

    /// <summary>
    ///     The same state, for a value that can be an object that the store does
    ///     not know also.
    /// </summary>
    public LoopState OrAnUnknownObject =>
        this.Objects.Contains(MadeObject.Unknown) ? this : this.WithObjects(this.Objects.Add(MadeObject.Unknown));

    /// <summary>The same state, for a collection that also holds the given objects.</summary>
    public LoopState Holding(ImmutableArray<MadeObject> more) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: this.IsContainer,
            objects: this.Objects,
            held: Union(first: this.Held, second: more));

    /// <summary>The same state, for a value that is one of the given objects.</summary>
    public LoopState WithObjects(ImmutableArray<MadeObject> objects) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: this.IsContainer,
            objects: objects,
            held: this.Held);

    /// <summary>
    ///     The state of a name of an object, with the state that the store has
    ///     for the object.
    /// </summary>
    /// <param name="stored">The state of the store.</param>
    /// <param name="all">
    ///     True for a name whose objects the store has, each of them. It gets the
    ///     members of the store, which have each change to those objects. Each
    ///     other value keeps its own members also.
    /// </param>
    public LoopState RefreshedFrom(LoopState stored, bool all) =>
        new(
            pending: this.Pending || stored.Pending,
            linked: this.Linked || stored.Linked,
            members: all
                ? stored.Members ?? this.Members
                : Construction.Merge(first: this.Members, second: stored.Members),
            content: this.content == null
                ? stored.content
                : stored.content == null
                    ? this.content
                    : this.content.Join(stored.content),
            origin: this.Origin,
            functions: this.Functions.AddRange(stored.Functions.Where(function => !this.Functions.Contains(function))),
            queries: this.Queries.AddRange(stored.Queries.Where(query => !this.Queries.Contains(query))),
            isContainer: stored.Members != null ? stored.IsContainer : this.IsContainer,
            objects: this.Objects,
            held: Union(first: this.Held, second: stored.Held));

    // A value can be one of two, where only one is an object of the store. The value can then be
    // an object that the store does not know. A pair from a dictionary is such an object.
    private static ImmutableArray<MadeObject> AnUnknownObject(LoopState first, LoopState second) =>
        first.Objects.IsEmpty != second.Objects.IsEmpty
        && (first.Objects.IsEmpty ? first : second) is { Members: not null } or { Pending: true }
            ? ImmutableArray.Create(MadeObject.Unknown)
            : NoObjects;

    private static ImmutableArray<MadeObject> Union(
        ImmutableArray<MadeObject> first,
        ImmutableArray<MadeObject> second) =>
        second.IsEmpty
            ? first
            : first.IsEmpty
                ? second
                : first.AddRange(second.Where(made => !first.Contains(made)));

    /// <summary>The state of a value that this expression holds.</summary>
    public LoopState Content => this.content ?? None;

    public bool ContentPending => this.content?.Pending ?? false;

    public bool ContentLinked => this.content?.Linked ?? false;

    /// <summary>A container whose values have this state.</summary>
    public LoopState AsContent =>
        new(
            pending: false,
            linked: false,
            members: null,
            content: this.IsNone ? null : this,
            origin: this.Origin,
            functions: ImmutableArray<FunctionValue>.Empty,
            queries: ImmutableArray<QueryValue>.Empty,
            isContainer: false,
            objects: NoObjects,
            held: NoObjects);

    /// <summary>
    ///     The state of a value that the code can get only after the close, as
    ///     the value of a firing is. Nothing reads that value before the close,
    ///     thus no part of it is pending, and a query in it runs after the close.
    ///     Its links stay.
    /// </summary>
    public LoopState AfterTheClose =>
        this.IsNone
            ? this
            : new LoopState(
                pending: false,
                linked: this.Linked,
                members: this.Members?.Select(static state => state.AfterTheClose),
                content: this.content?.AfterTheClose,
                origin: this.Origin,
                functions: ImmutableArray<FunctionValue>.Empty,
                queries: ImmutableArray<QueryValue>.Empty,
                isContainer: this.IsContainer,
                objects: NoObjects,
                held: NoObjects);

    /// <summary>True when the analyzer knows nothing about the expression.</summary>
    public bool IsNone =>
        !this.Pending
        && !this.Linked
        && this.Members == null
        && this.content == null
        && this.Functions.IsEmpty
        && this.Queries.IsEmpty;

    /// <summary>
    ///     The same state for a value that contains the objects which have the
    ///     members.
    /// </summary>
    public LoopState AsContainer => this.IsContainer || this.Members == null ? this : this.WithContainer(true);

    /// <summary>
    ///     The same state for one element of a value that contains objects. The
    ///     element can be one of the objects that have the members.
    /// </summary>
    public LoopState AsElement => this.IsContainer ? this.WithContainer(false) : this;

    /// <summary>
    ///     The state of one element of this collection. A collection with no
    ///     members has no mark of a container, and its element is one of the
    ///     objects that it holds also.
    /// </summary>
    public LoopState AsOneOfItsElements =>
        this.IsContainer || this.Held.IsEmpty ? this.AsElement : this.WithContainer(false);

    /// <summary>The state of a delegate that is one known function.</summary>
    public static LoopState OfFunction(FunctionValue function) =>
        new(
            pending: false,
            linked: false,
            members: null,
            content: null,
            origin: null,
            functions: ImmutableArray.Create(function),
            queries: ImmutableArray<QueryValue>.Empty,
            isContainer: false,
            objects: NoObjects,
            held: NoObjects);

    public LoopState Join(LoopState other)
    {
        if (other is { IsNone: true, Objects.IsEmpty: true, Held.IsEmpty: true })
        {
            return this;
        }

        if (this.IsNone && this.Objects.IsEmpty && this.Held.IsEmpty)
        {
            return other;
        }

        return new LoopState(
            pending: this.Pending || other.Pending,
            linked: this.Linked || other.Linked,
            members: Construction.Merge(
                first: FilledParts(known: this, other: other),
                second: FilledParts(known: other, other: this)),
            content: this.content == null
                ? other.content
                : other.content == null
                    ? this.content
                    : this.content.Join(other.content),
            origin: this.Origin ?? other.Origin,
            functions: this.Functions.IsEmpty
                ? other.Functions
                : this.Functions.AddRange(other.Functions.Where(function => !this.Functions.Contains(function))),
            queries: this.Queries.IsEmpty
                ? other.Queries
                : this.Queries.AddRange(other.Queries.Where(query => !this.Queries.Contains(query))),
            // The value is a container only when each state that has members says so. A local
            // that is a list on one path and an object on a second path can be the object.
            isContainer: this.Members == null
                ? other.IsContainer
                : other.Members == null
                    ? this.IsContainer
                    : this.IsContainer && other.IsContainer,
            objects: Union(
                first: Union(first: this.Objects, second: other.Objects),
                second: AnUnknownObject(first: this, second: other)),
            held: Union(first: this.Held, second: other.Held));
    }

    // A part of a pair that only one of two states knows gets the state of the other one also.
    private static Construction? FilledParts(LoopState known, LoopState other) =>
        known.Members is { HasParts: true } members ? members.FillParts(other) : known.Members;

    public LoopState WithPending(bool pending) =>
        new(
            pending: pending,
            linked: this.Linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: this.IsContainer,
            objects: this.Objects,
            held: this.Held);

    public LoopState WithLinked(bool linked) =>
        new(
            pending: this.Pending,
            linked: linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: this.IsContainer,
            objects: this.Objects,
            held: this.Held);

    public LoopState WithMembers(Construction? members) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: this.IsContainer,
            objects: this.Objects,
            held: this.Held);

    /// <summary>The same state for a value that is also the given query.</summary>
    public LoopState WithQuery(QueryValue query) =>
        this.Queries.Contains(query)
            ? this
            : new LoopState(
                pending: this.Pending,
                linked: this.Linked,
                members: this.Members,
                content: this.content,
                origin: this.Origin,
                functions: this.Functions,
                queries: this.Queries.Add(query),
                isContainer: this.IsContainer,
                objects: this.Objects,
                held: this.Held);

    /// <summary>A state that has only the queries of this one.</summary>
    public LoopState OnlyQueries =>
        this.Queries.IsEmpty
            ? None
            : new LoopState(
                pending: false,
                linked: false,
                members: null,
                content: null,
                origin: null,
                functions: ImmutableArray<FunctionValue>.Empty,
                queries: this.Queries,
                isContainer: false,
                objects: NoObjects,
                held: NoObjects);

    private LoopState WithContainer(bool isContainer) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: isContainer,
            // A container holds the objects that the value is. An element is one of the objects
            // that its container holds.
            objects: isContainer ? NoObjects : this.Held,
            held: isContainer ? Union(first: this.Objects, second: this.Held) : NoObjects);

    public LoopState WithContent(LoopState content) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: this.Members,
            content: content.IsNone ? null : content,
            origin: this.Origin,
            functions: this.Functions,
            queries: this.Queries,
            isContainer: this.IsContainer,
            objects: this.Objects,
            held: this.Held);

    /// <summary>
    ///     The same state with each origin changed: the origin of the value, of
    ///     its content, and of each of its members. The function gets null for a
    ///     part with no origin.
    /// </summary>
    public LoopState WithOrigins(Func<ISymbol?, ISymbol?> map) =>
        // An object of the store with no state can get a state after this, and that state then
        // has the origin of the object.
        this.IsNone && this.Objects.IsEmpty
            ? this
            : new LoopState(
                pending: this.Pending,
                linked: this.Linked,
                members: this.Members?.Select(state => state.WithOrigins(map)),
                content: this.content?.WithOrigins(map),
                origin: map(this.Origin),
                functions: this.Functions,
                queries: this.Queries,
                isContainer: this.IsContainer,
                objects: this.Objects,
                held: this.Held);

    /// <summary>
    ///     The same state, for a value that can be a second object where it can be
    ///     an object of the store. Each part of the state gets its second objects
    ///     also.
    /// </summary>
    /// <param name="second">Gives the second object. Null for an object that has none.</param>
    public LoopState WithAlso(Func<MadeObject, MadeObject?> second) =>
        this.IsNone && this.Objects.IsEmpty && this.Held.IsEmpty
            ? this
            : new LoopState(
                pending: this.Pending,
                linked: this.Linked,
                members: this.Members?.Select(state => state.WithAlso(second)),
                content: this.content?.WithAlso(second),
                origin: this.Origin,
                functions: this.Functions,
                queries: this.Queries,
                isContainer: this.IsContainer,
                objects: WithAlso(objects: this.Objects, second: second),
                held: WithAlso(objects: this.Held, second: second));

    private static ImmutableArray<MadeObject> WithAlso(
        ImmutableArray<MadeObject> objects,
        Func<MadeObject, MadeObject?> second)
    {
        ImmutableArray<MadeObject> all = objects;

        foreach (MadeObject made in objects)
        {
            if (second(made) is { } also && !all.Contains(also))
            {
                all = all.Add(also);
            }
        }

        return all;
    }

    /// <summary>The same state, with the one origin for each of its parts.</summary>
    public LoopState From(ISymbol origin) => this.WithOrigins(_ => origin);

    /// <summary>The same state, with the origin for each part that has none.</summary>
    public LoopState FromIfUnknown(ISymbol? origin) =>
        origin == null ? this : this.WithOrigins(known => known ?? origin);
}
