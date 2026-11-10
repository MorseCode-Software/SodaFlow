using System.Runtime.CompilerServices;

namespace SodaFlow;

/// <summary>
///     Gives the ids of the transactions, one after the other. It never gives
///     zero.
/// </summary>
/// <remarks>
///     <para>
///         Zero is the id of a root transaction that has no id, and it is the
///         owner of a <see cref="CaptureSlot{T}" /> that holds no value. A
///         transaction with the id zero gets a value from each empty slot that it
///         reads.
///     </para>
///     <para>
///         The count goes from the largest value to the smallest value with no
///         exception, and it gets to zero after 2^64 ids. That takes thousands of
///         years. The test against zero costs less than an incorrect value costs.
///     </para>
///     <para>
///         The caller holds the count, and this class holds none. Thus, the count
///         of the transactions stays one static field of a primitive type, and a
///         test can start a sequence at each value.
///     </para>
/// </remarks>
internal static class TransactionIdSequence
{
    /// <summary>
    ///     Gives the id that comes after <paramref name="last" />, and puts that id
    ///     in <paramref name="last" />.
    /// </summary>
    /// <param name="last">The last id that this sequence gave.</param>
    // Each transaction that gets an id calls this. Without the attribute, the JIT of .NET 10 keeps
    // the call.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long Next(ref long last)
    {
        unchecked
        {
            long id = ++last;

            return id != 0 ? id : ++last;
        }
    }
}
