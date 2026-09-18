using System;

namespace SodaFlow;

internal sealed class LazyBehavior<T> : Behavior<T>
{
    private Lazy<T>? lazyInitialValue;

    internal LazyBehavior(TransactionInternal trans, Stream<T> stream, Lazy<T> lazyInitialValue)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and this class overrides
        // that method to make sure that the value is set before the return.
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
            // A transaction must not change the internal parts of
            // SodaFlow.
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
