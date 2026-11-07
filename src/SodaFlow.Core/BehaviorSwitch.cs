using System;

namespace SodaFlow;

// SwitchB and SwitchC. The switch listens to the value of the outer behavior, and each value it
// gets selects a behavior. A selection listens to the value of that behavior into the output,
// and the output follows it until the next selection. The switch is a ValueListener on the
// outer behavior, and a selection is a ValueListener on the selected behavior. Before, the
// switch built a Value and a listener on it for the outer behavior. Each selection built the
// same for the behavior it selected. The Value, the listener on it, and the closures of the
// handler and of the last action were most of the cost of a switch.
//
// SwitchC selects the behavior of each cell through the select function. Before, it mapped the
// outer cell to a cell of behaviors and switched on that, which built a cell that only the
// switch read. The switch holds its own output, thus the projection does not hold.
internal sealed class BehaviorSwitch<T, T2>(
    in TransactionInternal trans,
    in Stream<T> @out,
    in Behavior<T2> outer,
    in Func<T2, Behavior<T>> select,
    in SwitchKeepListenersAlive keepListenersAlive)
    : ValueListener<T2>(trans: trans, source: outer, target: @out.Node)
{
    private readonly MutableListener current = new();
    private readonly SwitchKeepListenersAlive keepListenersAlive = keepListenersAlive;
    private readonly Stream<T> @out = @out;
    private readonly Func<T2, Behavior<T>> select = select;

    // The initial value of the output is the value of the initial selection. The sample reads
    // it through sampleInitial, which samples the selected behavior and does not build one.
    // SwitchB holds the output in a behavior, and SwitchC holds it in a cell.
    internal static (Stream<T> Out, LazySample<T> InitialValue) Create(
        TransactionInternal trans,
        Behavior<T2> outer,
        Func<T2, Behavior<T>> select,
        Func<T2, T> sampleInitial)
    {
        LazySample<T> initialValue = outer.SampleLazyMapped(trans: trans, f: sampleInitial);
        SwitchKeepListenersAlive keepListenersAlive = new(outer.KeepListenersAlive);
        Stream<T> @out = new(keepListenersAlive);

        BehaviorSwitch<T, T2> @switch =
            new(trans: trans, @out: @out, outer: outer, select: select, keepListenersAlive: keepListenersAlive);

        return (Out: @out.UnsafeAttachListener(@switch).UnsafeAttachListener(@switch.current),
            InitialValue: initialValue);
    }

    protected override void Consume(TransactionInternal trans, T2 value) =>
        trans.Last(new Selection(trans: trans, @switch: this, source: this.select(value)));

    // The new behavior gets its listener now, thus its value goes to the output in this
    // transaction. The previous listener stays until the commit. A transaction that fails
    // discards the update of the outer behavior. Thus, it must discard the switch too: the
    // discard removes the new listener and keeps the previous one.
    //
    // The previous listener cannot send a stale value after this point. Its arrivals link into
    // the output node, thus they have a lower rank. The drain ran them before this selection,
    // which the switch makes on the output node. A dependency of an inner behavior on the
    // output is a cycle, which Link refuses. FailedTransactionTests holds the two cases.
    private sealed class Selection(
        in TransactionInternal trans,
        in BehaviorSwitch<T, T2> @switch,
        in Behavior<T> source)
        : ValueListener<T>(trans: trans, source: source, target: @switch.@out.Node), ILastAction
    {
        private readonly BehaviorSwitch<T, T2> @switch = @switch;

        // The set stops the previous listener, which the mutable listener holds. The keep-alive
        // set moves its listeners to the root of the new behavior at the same time.
        void ILastAction.Commit()
        {
            this.@switch.current.SetListener(this);
            this.@switch.keepListenersAlive.SetInner(this.Source.KeepListenersAlive);
        }

        void ILastAction.Discard() => this.Unlisten();

        protected override void Consume(TransactionInternal trans, T value) =>
            this.@switch.@out.Send(trans: trans, a: value);
    }
}
