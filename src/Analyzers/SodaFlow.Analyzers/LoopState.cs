namespace SodaFlow.Analyzers;

/// <summary>
///     What the analyzer knows about one expression in a loop region.
/// </summary>
/// <param name="Pending">
///     The initial value of the expression reads the placeholder's current value.
///     A read of it before the close throws.
/// </param>
/// <param name="Linked">
///     The graph node of the expression is reachable from the placeholder through
///     links. A close with such a value is a dependency cycle.
/// </param>
/// <param name="ContentPending">
///     The values that the expression holds, for a cell of cells or a cell of
///     streams, are pending.
/// </param>
/// <param name="ContentLinked">
///     The values that the expression holds are linked to the placeholder.
/// </param>
internal readonly struct LoopState(in bool Pending, in bool Linked, in bool ContentPending, in bool ContentLinked)
{
    // A struct and not a record, because netstandard2.0 has no IsExternalInit for init accessors.
    public bool Pending { get; } = Pending;

    public bool Linked { get; } = Linked;

    public bool ContentPending { get; } = ContentPending;

    public bool ContentLinked { get; } = ContentLinked;

    public static readonly LoopState None = default;

    public static readonly LoopState PendingPlaceholder = new(
        Pending: true,
        Linked: true,
        ContentPending: false,
        ContentLinked: false);

    public static readonly LoopState StreamPlaceholder = new(
        Pending: false,
        Linked: true,
        ContentPending: false,
        ContentLinked: false);

    /// <summary>The state of a value that this expression holds.</summary>
    public LoopState Content => new(
        Pending: this.ContentPending,
        Linked: this.ContentLinked,
        ContentPending: false,
        ContentLinked: false);

    /// <summary>A container whose values have this state.</summary>
    public LoopState AsContent => new(
        Pending: false,
        Linked: false,
        ContentPending: this.Pending,
        ContentLinked: this.Linked);

    public LoopState Join(LoopState other) =>
        new(
            Pending: this.Pending || other.Pending,
            Linked: this.Linked || other.Linked,
            ContentPending: this.ContentPending || other.ContentPending,
            ContentLinked: this.ContentLinked || other.ContentLinked);

    public LoopState WithPending(bool pending) =>
        new(
            Pending: pending,
            Linked: this.Linked,
            ContentPending: this.ContentPending,
            ContentLinked: this.ContentLinked);

    public LoopState WithLinked(bool linked) =>
        new(
            Pending: this.Pending,
            Linked: linked,
            ContentPending: this.ContentPending,
            ContentLinked: this.ContentLinked);
}
