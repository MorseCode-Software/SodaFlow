using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Behavior{T}" />. It is equal to the
///     <see cref="Behavior{T}" /> that the loop supplies.
/// </summary>
/// <typeparam name="T">The type of values in the behavior loop.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class LoopedBehavior<T> : Behavior<T>
{
    private readonly LoopedStream<T> streamLoop;

    private LazySample<T>? lazyInitialValue;

    internal LoopedBehavior()
        : this(new LoopedStream<T>())
    {
    }

    private LoopedBehavior(LoopedStream<T> streamLoop)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and this class overrides
        // that method to make sure that the value is set before the return.
        : base(stream: streamLoop, initialValue: default!) =>
        this.streamLoop = streamLoop;

    internal void Loop(TransactionInternal trans, Behavior<T> b)
    {
        this.streamLoop.Loop(trans: trans, stream: b.Updates());
        this.lazyInitialValue = b.SampleLazy(trans);
    }

    internal override T SampleNoTransaction()
    {
        if (!this.streamLoop.IsAssigned)
        {
            throw new InvalidOperationException("BehaviorLoop was sampled before it was looped.");
        }

        this.EnsureValueIsCreated();

        return this.ValueProperty;
    }

    // The transaction that forces the sample can fail after that, and the value then came from a
    // sample that the failure forgets. Thus, the force registers a last action: its commit
    // releases the sample, which the behavior does not need after a value, and its discard makes
    // the behavior use its initial value again, so the next read forces the sample again.
    private void EnsureValueIsCreated()
    {
        if (!this.UsingInitialValue || this.lazyInitialValue is not { } sample)
        {
            return;
        }

        // Each caller runs in a transaction: a read through SampleImpl, or a sample phase.
        TransactionInternal trans = TransactionInternal.CurrentTransaction();

        this.ValueProperty = sample.Force(trans);
        trans.Last(commit: this.ReleaseInitialValue, discard: this.UseInitialValueAgain);
    }

    private void ReleaseInitialValue() => this.lazyInitialValue = null;
}
