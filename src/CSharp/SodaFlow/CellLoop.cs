using System;
using System.Runtime.CompilerServices;

namespace SodaFlow
{
    /// <summary>
    ///     A forward reference for a <see cref="Cell{T}" /> equivalent to the <see cref="Cell{T}" /> that is
    ///     referenced.
    /// </summary>
    /// <typeparam name="T">The type of values in the cell loop.</typeparam>
    public class CellLoop<T> : LoopedCell<T>
    {
        private TransactionInternal transaction;

        private readonly object isLoopedLock = new object();
        private bool isLooped;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CellLoop{T}" /> class, a forward reference to a
        ///     cell that has not been defined yet.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if there is no explicit transaction running, or, at the end of that transaction,
        ///     if <see cref="Loop" /> was never called on this instance.
        /// </exception>
        /// <remarks>
        ///     A loop only makes sense within a single transaction, so one must be running - create it
        ///     with <see cref="Transaction.Run{T}(Func{T})" /> or
        ///     <see cref="Transaction.RunVoid(Action)" />. Resolve the loop by calling
        ///     <see cref="Loop" /> before that transaction ends; a loop left unresolved is a bug rather
        ///     than a no-op, so it is reported as one.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public CellLoop()
        {
            this.transaction = TransactionInternal.GetCurrentTransaction();

            if (this.transaction == null)
            {
                throw new InvalidOperationException("Loop must be created within an explicit transaction.");
            }

            this.transaction.Last(
                () =>
                {
                    if (this.transaction != null)
                    {
                        this.transaction = null;

                        throw new InvalidOperationException("Loop was not looped.");
                    }
                });
        }

        /// <summary>
        ///     Resolve the loop to specify what the <see cref="CellLoop{T}" /> was a forward reference to.  This method
        ///     must be called inside the same transaction as the one in which this <see cref="CellLoop{T}" /> instance was
        ///     created and used.
        ///     This requires an explicit transaction to be created with <see cref="Transaction.Run{T}(Func{T})" /> or
        ///     <see cref="Transaction.RunVoid(Action)" />.
        /// </summary>
        /// <param name="c">The cell that was forward referenced.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Loop(Cell<T> c) =>
            TransactionInternal.Apply(
                (trans, _) =>
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

                    this.Loop(trans, c);

                    return UnitInternal.Value;
                });
    }
}
