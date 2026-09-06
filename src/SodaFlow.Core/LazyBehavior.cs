using System;

namespace SodaFlow;

internal sealed class LazyBehavior<T> : Behavior<T>
{
    private Lazy<T>? lazyInitialValue;

    internal LazyBehavior(TransactionInternal trans, Stream<T> stream, Lazy<T> lazyInitialValue)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class, and that value is only read by SampleNoTransaction(), which this class overrides to
        // ensure the value is set before returning.
        : base(stream: stream, initialValue: default!)
    {
        this.lazyInitialValue = new Lazy<T>(() => GuardAgainstSend(trans: trans, v: lazyInitialValue));

        trans.Sample(this.EnsureValueIsCreated);
    }

    private static T GuardAgainstSend(TransactionInternal trans, Lazy<T> v)
    {
        trans.InCallback++;

        try
        {
            // Don't allow transactions to interfere with SodaFlow
            // internals.
            return v.Value;
        }
        finally
        {
            trans.InCallback--;
        }
    }

    protected override void NotUsingInitialValue()
    {
        base.NotUsingInitialValue();

        this.lazyInitialValue = null;
    }

    internal override T SampleNoTransaction()
    {
        this.EnsureValueIsCreated();

        return this.ValueProperty;
    }

    private void EnsureValueIsCreated()
    {
        if (this.UsingInitialValue && this.lazyInitialValue != null)
        {
            this.ValueProperty = this.lazyInitialValue.Value;
            this.lazyInitialValue = null;
        }
    }
}
