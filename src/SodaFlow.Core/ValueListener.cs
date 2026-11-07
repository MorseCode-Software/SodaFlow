using System;

namespace SodaFlow;

// A listener on the value of a behavior. It gives the current value in the transaction of the
// listen, and then each update, to Consume. It calls Consume one time in a transaction, with the
// last value of that transaction. Behavior.Value is this with an output stream as the consumer.
// A switch uses it with no stream: each behavior that the switch selects gets one, and its
// consumer sends into the output of the switch. Before, each selection built a Value and a
// listener on it. The Value was a stream, a listener, a handler, and two closures, and the
// selection used none of them.
//
// The ranks are the ranks of Value. The updates arrive at a bare node that links into the node
// of the consumer. The first arrival in a transaction queues the send on the node of the
// consumer, which ranks above the node of the arrivals. Thus, the send runs after the last
// arrival of the transaction, and a second arrival in the same transaction replaces the value
// before the send. The initial value arrives from an action at rank zero. A new node ranks below
// all other nodes. Thus, the initial value also arrives when a caller listens during a drain. A
// listen in a loop reads the behavior after the loop has its definition, for the same cause. A
// transaction that fails before the send runs discards the send and clears the value. An
// arrival queues the send only when no value waits.
internal abstract class ValueListener<T> : StreamAction<T>
{
    // The action of the link that gives the consumer its rank. The node of the arrivals never
    // sends, thus nothing calls it.
    private static readonly Action<TransactionInternal, T> NoAction = static (_, _) => { };

    private readonly Node<T> arrivals = new();
    private readonly Node target;
    private T? accum;
    private bool accumValid;

    protected ValueListener(TransactionInternal trans, Behavior<T> source, Node target)
    {
        this.Source = source;
        this.target = target;

        // The link gives the node of the consumer its rank. The send does not use it.
        this.arrivals.Link(trans: trans, action: NoAction, target: target);

        trans.Prioritized(new InitialEntry(this));

        source.Updates().Listen(target: this.arrivals, trans: trans, action: this, suppressEarlierFirings: false);
    }

    protected Behavior<T> Source { get; }

    protected abstract void Consume(TransactionInternal trans, T value);

    /// <inheritdoc />
    // The arrival of one update, or of the initial value.
    public override void Fire(TransactionInternal trans, T value)
    {
        this.accum = value;

        if (!this.accumValid)
        {
            this.accumValid = true;
            trans.Prioritized(new SendEntry(this));
        }
    }

    private void Send(TransactionInternal trans)
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed - accum has a value when a send is queued.
        T value = this.accum!;
        this.Clear();
        this.Consume(trans: trans, value: value);
    }

    private void Clear()
    {
        this.accumValid = false;
        this.accum = default;
    }

    private sealed class InitialEntry(in ValueListener<T> listener) : TransactionInternal.Entry
    {
        private readonly ValueListener<T> listener = listener;

        public override void Execute(TransactionInternal trans) =>
            this.listener.Fire(trans: trans, value: this.listener.Source.SampleNoTransaction());
    }

    private sealed class SendEntry(in ValueListener<T> listener) : TransactionInternal.Entry(listener.target)
    {
        private readonly ValueListener<T> listener = listener;

        public override void Execute(TransactionInternal trans) => this.listener.Send(trans);

        public override void Discard() => this.listener.Clear();
    }
}
