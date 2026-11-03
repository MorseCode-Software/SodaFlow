using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Behavior{T}" />. It is equal to the
///     behavior that closes the loop.
/// </summary>
/// <typeparam name="T">The type of values in the behavior loop.</typeparam>
/// <remarks>
///     A loop block makes one and closes it when the block returns. The
///     explicit form is a call to the constructor in a transaction, and a call to
///     <see cref="Loop(Behavior{T})" /> in the same transaction. The type is a
///     <see cref="LoopedBehavior{T}" />, thus no code can read its value before the
///     close.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class BehaviorLoop<T> : LoopedBehavior<T>
{
    private readonly object isLoopedLock = new();
    private readonly LoopedStream<T> streamLoop;
    private bool isLooped;

    private LazySample<T>? lazyInitialValue;
    private TransactionInternal? transaction;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BehaviorLoop{T}" /> class, a
    ///     forward reference to a behavior with no definition at this point.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when there is no explicit transaction open, or, at the end of that
    ///     transaction, if <see cref="Loop(Behavior{T})" /> was never called on this instance.
    /// </exception>
    /// <remarks>
    ///     A loop only makes sense in a single transaction, thus one must be open -
    ///     make it with <c>Transaction.Run</c> or <c>Transaction.RunVoid</c>. Resolve
    ///     the loop by calling <see cref="Loop(Behavior{T})" /> before that transaction ends. A loop
    ///     with no resolution is a defect, and not a no-op, thus this reports it as a
    ///     defect.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public BehaviorLoop()
        : this(new LoopedStream<T>())
    {
        this.transaction = TransactionInternal.GetCurrentTransaction();

        if (this.transaction == null)
        {
            throw new InvalidOperationException("Loop must be created within an explicit transaction.");
        }

        this.transaction.Last(() =>
        {
            if (this.transaction != null)
            {
                this.transaction = null;

                throw new InvalidOperationException("Loop was not looped.");
            }
        });
    }

    private BehaviorLoop(LoopedStream<T> streamLoop)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and this class overrides
        // that method to make sure that the value is set before the return.
        : base(stream: streamLoop, initialValue: default!) =>
        this.streamLoop = streamLoop;

    /// <summary>
    ///     Resolve the loop to specify what the <see cref="BehaviorLoop{T}" /> was a
    ///     forward reference to.  This method must run in the same transaction as the
    ///     one that made this <see cref="BehaviorLoop{T}" /> created and used.
    ///     This needs an explicit transaction from <c>Transaction.Run</c> or
    ///     <c>Transaction.RunVoid</c>.
    /// </summary>
    /// <param name="b">The behavior of the forward reference.</param>
    /// <returns>
    ///     The <see cref="LoopCloser" />, which converts a value that depends on this
    ///     loop to the plain type.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public LoopCloser Loop(Behavior<T> b) =>
        TransactionInternal.Apply((trans, _) =>
        {
            lock (this.isLoopedLock)
            {
                if (this.isLooped)
                {
                    throw new InvalidOperationException("Loop was looped more than once.");
                }
            }

            if (trans != this.transaction)
            {
                this.transaction = null;

                throw new InvalidOperationException(
                    "Loop must be looped in the same transaction that it was created in.");
            }

            return this.Loop(trans: trans, b: b);
        });

    // A loop block closes the loop through this, in the transaction that made it. Thus, it
    // needs none of the checks above, but it marks the loop closed so that the check at the end
    // of the transaction passes.
    internal LoopCloser Loop(TransactionInternal trans, Behavior<T> b)
    {
        lock (this.isLoopedLock)
        {
            if (this.isLooped)
            {
                throw new InvalidOperationException("Loop was looped more than once.");
            }

            this.isLooped = true;
        }

        this.transaction = null;
        this.streamLoop.Loop(trans: trans, stream: b.Updates());
        this.lazyInitialValue = b.SampleLazy(trans);

        return LoopCloser.Instance;
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
