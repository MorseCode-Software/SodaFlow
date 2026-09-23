using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference for a <see cref="Behavior{T}" />, equivalent to the
///     <see cref="Behavior{T}" /> that it stands for.
/// </summary>
/// <typeparam name="T">The type of values in the behavior loop.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class BehaviorLoop<T> : LoopedBehavior<T>
{
    private readonly object isLoopedLock = new();
    private bool isLooped;
    private TransactionInternal? transaction;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BehaviorLoop{T}" /> class, a forward reference to a
    ///     behavior with no definition at this point.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when there is no explicit transaction open, or, at the end of that transaction,
    ///     if <see cref="Loop" /> was never called on this instance.
    /// </exception>
    /// <remarks>
    ///     A loop only makes sense in a single transaction, thus one must be open - make it
    ///     with <see cref="Transaction.Run{T}(Func{T})" /> or
    ///     <see cref="Transaction.RunVoid(Action)" />. Resolve the loop by calling
    ///     <see cref="Loop" /> before that transaction ends. A loop with no resolution is a defect, and not
    ///     than a no-op, thus this reports it as one.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public BehaviorLoop()
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

    /// <summary>
    ///     Resolve the loop to specify what the <see cref="BehaviorLoop{T}" /> was a forward reference to.  This method
    ///     must run in the same transaction as the one that made this <see cref="BehaviorLoop{T}" />
    ///     created and used.
    ///     This needs an explicit transaction from <see cref="Transaction.Run{T}(Func{T})" /> or
    ///     <see cref="Transaction.RunVoid(Action)" />.
    /// </summary>
    /// <param name="b">The behavior of the forward reference.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Loop(Behavior<T> b) =>
        TransactionInternal.Apply((trans, _) =>
        {
            lock (this.isLoopedLock)
            {
                if (this.isLooped)
                {
                    throw new InvalidOperationException("Loop was looped more than once.");
                }

                this.isLooped = true;
            }

            if (trans != this.transaction)
            {
                this.transaction = null;

                throw new InvalidOperationException(
                    "Loop must be looped in the same transaction that it was created in.");
            }

            this.transaction = null;

            this.Loop(trans: trans, b: b);

            return UnitInternal.Value;
        });
}
