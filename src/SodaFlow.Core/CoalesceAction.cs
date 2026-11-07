using System;

namespace SodaFlow;

/// <summary>
///     The action that folds the firings of one transaction into one and sends
///     that one at the output node. A coalesce operation and a sink each hold one.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class CoalesceAction<T> : StreamAction<T>
{
    private readonly Action discard;
    private readonly Func<T, T, T> f;
    private readonly Stream<T> @out;
    private readonly Action<TransactionInternal> send;
    private T? accum;
    private bool accumValid;

    internal CoalesceAction(Func<T, T, T> f, Stream<T> @out)
    {
        this.f = f;
        this.@out = @out;

        // Two delegates for the life of the action, and not two for each transaction that fires.
        this.send = this.Send;
        this.discard = this.Discard;
    }

    public override void Fire(TransactionInternal trans, T value)
    {
        if (this.accumValid)
        {
            // ReSharper disable once NullableWarningSuppressionIsUsed - accum will always have a non-null value
            // when accumValid is true.
            this.accum = this.f(arg1: this.accum!, arg2: value);
        }
        else
        {
            this.accum = value;
            this.accumValid = true;
            trans.Prioritized(node: this.@out.Node, action: this.send, discard: this.discard);
        }
    }

    private void Send(TransactionInternal trans)
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed - accum has a value when a send is queued.
        this.@out.Send(trans: trans, a: this.accum!);
        this.Discard();
    }

    // A transaction that fails can stop before the send runs. Then accumValid must go back to
    // false, because Fire queues the send only when it is false. Otherwise, each subsequent
    // value goes into the accumulator and the stream does not fire again.
    private void Discard()
    {
        this.accumValid = false;
        this.accum = default;
    }
}
