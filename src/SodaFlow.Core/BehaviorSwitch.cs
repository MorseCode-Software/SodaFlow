using System;

namespace SodaFlow;

// SwitchB and SwitchC. The switch listens to the value of the outer behavior, and each value it
// gets selects a behavior. A selection listens to the value of that behavior, and the switch
// follows it until the next selection. The switch is a ValueListener on the outer behavior, and a
// selection is a ValueListener on the selected behavior. Before, the switch built a Value and a
// listener on it for the outer behavior. Each selection built the same for the behavior it
// selected. The Value, the listener on it, and the closures of the handler and of the last action
// were most of the cost of a switch.
//
// SwitchC selects the behavior of each cell through the select function. Before, it mapped the
// outer cell to a cell of behaviors and switched on that, which built a cell that only the switch
// read.
//
// The switch is the derivation of the behavior that it gives. Before, it sent into an output
// stream, and a lazy hold kept the value. A hold cannot unlink, because its value then becomes
// stale. With no listener on the updates, the switch unlinks from the outer behavior and from the
// selection after one value. A read then selects from the outer behavior as it is now.
internal sealed class BehaviorSwitch<T, T2>
    : ValueListener<T2>, IDerivation<T>, IDemandOwner
{
    private readonly MutableListener current = new();
    private readonly SwitchKeepListenersAlive keepListenersAlive;
    private readonly Func<T2, Behavior<T>> select;
    private DerivedCore<T> core;

    private BehaviorSwitch(
        TransactionInternal trans,
        Behavior<T2> outer,
        Func<T2, Behavior<T>> select,
        SwitchKeepListenersAlive keepListenersAlive,
        DemandStreamNode<T> node)
        : base(trans: trans, source: outer, target: node, giveInitial: true)
    {
        this.keepListenersAlive = keepListenersAlive;
        this.select = select;
        this.core = new DerivedCore<T>(keepListenersAlive: keepListenersAlive, node: node);
    }

    /// <inheritdoc />
    public IKeepListenersAlive KeepListenersAlive => this.core.KeepListenersAlive;

    /// <inheritdoc />
    public bool IsUnlinked => this.core.IsUnlinked;

    /// <inheritdoc />
    public long SourceVersion =>
        Math.Max(val1: this.Source.Version, val2: this.select(this.Source.SampleNoTransaction()).Version);

    /// <inheritdoc />
    public bool SourcesHavePending =>
        this.Source.HasPendingUpdate || this.select(this.Source.SampleNoTransaction()).HasPendingUpdate;

    // SwitchB holds the switch in a behavior, and SwitchC holds it in a cell.
    internal static BehaviorSwitch<T, T2> Create(
        TransactionInternal trans,
        Behavior<T2> outer,
        Func<T2, Behavior<T>> select)
    {
        DemandStreamNode<T> node = new();

        BehaviorSwitch<T, T2> @switch = new(
            trans: trans,
            outer: outer,
            select: select,
            keepListenersAlive: new SwitchKeepListenersAlive(outer.KeepListenersAlive),
            node: node);

        // The outer link is first, thus the node finds the switch as its owner. The registry stops
        // the two links when no behavior and no stream holds the switch.
        node.Attach(stream: @switch, listener: @switch.GetListenerWithWeakReference());
        node.Attach(stream: @switch, listener: @switch.current.GetListenerWithWeakReference());

        return @switch;
    }

    /// <inheritdoc />
    public void Bind(Behavior<T> behavior) => this.core.Bind(behavior);

    // The value of the selection at the end of this transaction, as the lazy sample of the hold
    // read it before.
    /// <inheritdoc />
    public T Initial() => this.select(this.Source.PendingOrCurrent()).SampleNoTransaction();

    /// <inheritdoc />
    // The stream holds the switch, and the switch holds the selection.
    public Stream<T> CreateUpdates(TransactionInternal trans) =>
        this.core.CreateUpdates(trans: trans, retained: this);

    /// <inheritdoc />
    public T Pull(bool pending)
    {
        if (pending && this.SourcesHavePending)
        {
            return this.select(this.Source.PendingOrCurrent()).PendingOrCurrent();
        }

        long version = this.SourceVersion;

        if (this.core.Cached(version).TryGetValue(out T cached))
        {
            return cached;
        }

        T value = this.select(this.Source.SampleNoTransaction()).SampleNoTransaction();
        this.core.SetCached(version: version, value: value);

        return value;
    }

    // The behavior takes its value from the outer behavior, and the switch selects again. The
    // selection gives no initial value, because the behavior has that value now. The switch uses the
    // selection immediately, and not at the commit. No previous selection has a link to keep, and a
    // transaction that fails keeps the listener that linked the switch again. An outer update in
    // this transaction comes again through the outer link, and selects after this.
    void IDemandOwner.Relinking(TransactionInternal trans)
    {
        this.core.Relinking();

        Selection selection = new(
            trans: trans,
            @switch: this,
            source: this.select(this.Source.SampleNoTransaction()),
            giveInitial: false);

        this.Use(selection);
    }

    void IDemandOwner.Unlinked(TransactionInternal trans) => this.core.Unlinked(trans);

    protected override void Consume(TransactionInternal trans, T2 value) =>
        trans.Last(new Selection(trans: trans, @switch: this, source: this.select(value), giveInitial: true));

    // The set stops the previous listener, which the mutable listener holds. The keep-alive set
    // moves its listeners to the root of the new behavior at the same time.
    private void Use(Selection selection)
    {
        this.current.SetListener(selection);
        this.keepListenersAlive.SetInner(selection.Selected.KeepListenersAlive);
    }

    // The new behavior gets its listener now, thus its value arrives in this transaction. The
    // previous listener stays until the commit. A transaction that fails discards the update of the
    // outer behavior. Thus, it must discard the switch too: the discard removes the new listener and
    // keeps the previous one.
    //
    // The previous listener cannot send a stale value after this point. Its arrivals link into the
    // node of the switch, thus they have a lower rank. The drain ran them before this selection,
    // which the switch makes on its node. A dependency of an inner behavior on the switch is a
    // cycle, which Link refuses. FailedTransactionTests holds the two cases.
    private sealed class Selection(
        in TransactionInternal trans,
        in BehaviorSwitch<T, T2> @switch,
        in Behavior<T> source,
        in bool giveInitial)
        : ValueListener<T>(trans: trans, source: source, target: @switch.core.Node, giveInitial: giveInitial),
            ILastAction
    {
        private readonly BehaviorSwitch<T, T2> @switch = @switch;

        internal Behavior<T> Selected => this.Source;

        // An unlink in the last phase sometimes comes before this commit. The selection then has a
        // link that nothing else removes, thus it removes that link. The next link of the switch
        // selects again.
        void ILastAction.Commit()
        {
            if (this.@switch.core.IsUnlinked)
            {
                this.Dispose();
                return;
            }

            this.@switch.Use(this);
        }

        void ILastAction.Discard() => this.Dispose();

        protected override void Consume(TransactionInternal trans, T value) =>
            this.@switch.core.Arrive(trans: trans, value: value);
    }
}
