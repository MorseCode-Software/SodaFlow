namespace SodaFlow.Analyzers;

/// <summary>What the analyzer knows about one expression in a loop region.</summary>
/// <remarks>
///     A class and not a struct. A state holds the state of the values it
///     contains: a cell of cells, a cell of streams, or a hold of an object.
/// </remarks>
internal sealed class LoopState
{
    public static readonly LoopState None = new(pending: false, linked: false, members: null, content: null);

    public static readonly LoopState
        PendingPlaceholder = new(pending: true, linked: true, members: null, content: null);

    public static readonly LoopState
        StreamPlaceholder = new(pending: false, linked: true, members: null, content: null);

    private readonly LoopState? content;

    public LoopState(bool pending, bool linked, bool contentPending, bool contentLinked)
        : this(
            pending: pending,
            linked: linked,
            members: null,
            content: contentPending || contentLinked
                ? new LoopState(pending: contentPending, linked: contentLinked, members: null, content: null)
                : null)
    {
    }

    private LoopState(bool pending, bool linked, Construction? members, LoopState? content)
    {
        this.Pending = pending;
        this.Linked = linked;
        this.Members = members;
        this.content = content;
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

    /// <summary>The state of a value that this expression holds.</summary>
    public LoopState Content => this.content ?? None;

    public bool ContentPending => this.content?.Pending ?? false;

    public bool ContentLinked => this.content?.Linked ?? false;

    /// <summary>A container whose values have this state.</summary>
    public LoopState AsContent => new(pending: false, linked: false, members: null, content: this.IsNone ? null : this);

    private bool IsNone => !this.Pending && !this.Linked && this.Members == null && this.content == null;

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
                    : this.content.Join(other.content));
    }

    public LoopState WithPending(bool pending) =>
        new(pending: pending, linked: this.Linked, members: this.Members, content: this.content);

    public LoopState WithLinked(bool linked) =>
        new(pending: this.Pending, linked: linked, members: this.Members, content: this.content);

    public LoopState WithMembers(Construction? members) =>
        new(pending: this.Pending, linked: this.Linked, members: members, content: this.content);

    public LoopState WithContent(LoopState content) =>
        new(
            pending: this.Pending,
            linked: this.Linked,
            members: this.Members,
            content: content.IsNone ? null : content);
}
