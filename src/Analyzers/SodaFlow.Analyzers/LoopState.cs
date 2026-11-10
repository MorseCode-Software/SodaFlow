using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>What the analyzer knows about one expression in a loop region.</summary>
/// <remarks>
///     A class and not a struct. A state holds the state of the values it
///     contains: a cell of cells, a cell of streams, or a hold of an object.
/// </remarks>
internal sealed class LoopState
{
    public static readonly LoopState None = new(
        pending: false,
        linked: false,
        members: null,
        content: null,
        origin: null,
        functions: ImmutableArray<FunctionValue>.Empty);

    public static readonly LoopState PendingPlaceholder = new(
        pending: true,
        linked: true,
        members: null,
        content: null,
        origin: null,
        functions: ImmutableArray<FunctionValue>.Empty);

    public static readonly LoopState StreamPlaceholder = new(
        pending: false,
        linked: true,
        members: null,
        content: null,
        origin: null,
        functions: ImmutableArray<FunctionValue>.Empty);

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
                    functions: ImmutableArray<FunctionValue>.Empty)
                : null,
            origin: null,
            functions: ImmutableArray<FunctionValue>.Empty)
    {
    }

    private LoopState(
        bool pending,
        bool linked,
        Construction? members,
        LoopState? content,
        ISymbol? origin,
        ImmutableArray<FunctionValue> functions)
    {
        this.Pending = pending;
        this.Linked = linked;
        this.Members = members;
        this.content = content;
        this.Origin = origin;
        this.Functions = functions;
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
            functions: ImmutableArray<FunctionValue>.Empty);

    /// <summary>True when the analyzer knows nothing about the expression.</summary>
    public bool IsNone =>
        !this.Pending && !this.Linked && this.Members == null && this.content == null && this.Functions.IsEmpty;

    /// <summary>The state of a delegate that is one known function.</summary>
    public static LoopState OfFunction(FunctionValue function) =>
        new(
            pending: false,
            linked: false,
            members: null,
            content: null,
            origin: null,
            functions: ImmutableArray.Create(function));

    public LoopState Join(LoopState other)
    {
        if (other.IsNone)
        {
            return this;
        }

        if (this.IsNone)
        {
            return other;
        }

        return new LoopState(
            pending: this.Pending || other.Pending,
            linked: this.Linked || other.Linked,
            members: Construction.Merge(first: this.Members, second: other.Members),
            content: this.content == null
                ? other.content
                : other.content == null
                    ? this.content
                    : this.content.Join(other.content),
            origin: this.Origin ?? other.Origin,
            functions: this.Functions.IsEmpty
                ? other.Functions
                : this.Functions.AddRange(other.Functions.Where(function => !this.Functions.Contains(function))));
    }

    public LoopState WithPending(bool pending) =>
        new(
            pending: pending,
            linked: this.Linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions);

    public LoopState WithLinked(bool linked) =>
        new(
            pending: this.Pending,
            linked: linked,
            members: this.Members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions);

    public LoopState WithMembers(Construction? members) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: members,
            content: this.content,
            origin: this.Origin,
            functions: this.Functions);

    public LoopState WithContent(LoopState content) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: this.Members,
            content: content.IsNone ? null : content,
            origin: this.Origin,
            functions: this.Functions);

    /// <summary>
    ///     The same state with each origin changed: the origin of the value, of
    ///     its content, and of each of its members. The function gets null for a
    ///     part with no origin.
    /// </summary>
    public LoopState WithOrigins(Func<ISymbol?, ISymbol?> map) =>
        this.IsNone
            ? this
            : new LoopState(
                pending: this.Pending,
                linked: this.Linked,
                members: this.Members?.Select(state => state.WithOrigins(map)),
                content: this.content?.WithOrigins(map),
                origin: map(this.Origin),
                functions: this.Functions);

    /// <summary>The same state, with the one origin for each of its parts.</summary>
    public LoopState From(ISymbol origin) => this.WithOrigins(_ => origin);

    /// <summary>The same state, with the origin for each part that has none.</summary>
    public LoopState FromIfUnknown(ISymbol? origin) =>
        origin == null ? this : this.WithOrigins(known => known ?? origin);
}
