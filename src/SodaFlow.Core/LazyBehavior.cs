namespace SodaFlow;

// A behavior whose initial value is a LazySample. The sample phase of the transaction that makes
// the behavior forces the sample, and so does a read that comes first. The transaction that
// forces the sample can fail after that, and the value then came from a sample that the failure
// forgets. Thus, the force registers a last action in that transaction. Its commit releases the
// sample, and its discard makes the behavior use its initial value again. The next read then
// forces the sample again and gets the committed value.
internal sealed class LazyBehavior<T> : Behavior<T>
{
    private LazySample<T>? lazyInitialValue;

    internal LazyBehavior(TransactionInternal trans, Stream<T> stream, LazySample<T> lazyInitialValue)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and this class overrides
        // that method to make sure that the value is set before the return.
        : base(stream: stream, initialValue: default!)
    {
        this.lazyInitialValue = lazyInitialValue;

        trans.Sample(this.EnsureValueIsCreated);
    }

    internal override T SampleNoTransaction()
    {
        this.EnsureValueIsCreated();

        return this.ValueProperty;
    }

    private void EnsureValueIsCreated()
    {
        if (!this.UsingInitialValue || this.lazyInitialValue is not { } sample)
        {
            return;
        }

        // Each caller runs in a transaction: the sample phase, or a read through SampleImpl.
        TransactionInternal trans = TransactionInternal.CurrentTransaction();

        // A transaction must not change the internal parts of SodaFlow.
        trans.InCallback++;

        try
        {
            this.ValueProperty = sample.Force(trans);
        }
        finally
        {
            trans.InCallback--;
        }

        trans.Last(commit: this.ReleaseInitialValue, discard: this.UseInitialValueAgain);
    }

    private void ReleaseInitialValue() => this.lazyInitialValue = null;
}
