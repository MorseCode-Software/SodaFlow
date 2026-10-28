namespace SodaFlow;

/// <summary>
///     A value that one input of an operation captured in a transaction, for the
///     step of that same transaction that puts the inputs together.
/// </summary>
/// <remarks>
///     <para>
///         The slot records the transaction that filled it, and a read in a
///         different transaction gives no value. A transaction that fails between
///         the capture and the read does not run the read, thus the slot keeps a
///         value that its input never committed. Before this record, the next read
///         gave that value. A lift, an apply, a snapshot of the latest values, or a
///         merge of two types then gave an incorrect result. That continued until
///         the input updated again.
///     </para>
///     <para>
///         The record is the id of the transaction and not a reference to it.
///         Thus, a slot does not keep the last transaction and its queues in memory
///         until the next capture.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the captured value.</typeparam>
internal struct CaptureSlot<T>
{
    private T? value;

    // Zero when the slot holds no value for a read. The id of a transaction is never zero.
    private long owner;

    /// <summary>Records the value that an input gave in a transaction.</summary>
    internal void Set(TransactionInternal trans, T value)
    {
        this.value = value;
        this.owner = trans.Id;
    }

    /// <summary>
    ///     Gives the value when <paramref name="trans" /> filled the slot, and
    ///     clears the slot in each case. Thus, a slot does not hold a second
    ///     reference to the last value of its input for the life of the operation.
    /// </summary>
    internal MaybeInternal<T> Take(TransactionInternal trans)
    {
        MaybeInternal<T> result = this.Peek(trans);
        this.value = default;
        this.owner = 0;
        return result;
    }

    /// <summary>
    ///     The same as <see cref="Take" />, and keeps the slot. An apply reads its two
    ///     inputs with this. It puts them together at the update of each one, and the
    ///     second read in one transaction must see the first value.
    /// </summary>
    internal MaybeInternal<T> Peek(TransactionInternal trans) =>
        // ReSharper disable once NullableWarningSuppressionIsUsed - Set put a value here when owner is the id of a
        // transaction.
        this.owner == trans.Id ? MaybeInternal.Some(this.value!) : MaybeInternal<T>.None;
}
