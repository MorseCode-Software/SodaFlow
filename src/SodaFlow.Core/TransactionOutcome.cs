using System.Threading;

namespace SodaFlow;

/// <summary>
///     The outcome of one transaction, for code that runs after the transaction on a
///     different thread.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Failed" /> tells that code that a value it carries from the
///         transaction can be different from the value that the cell holds. The
///         correct response is a read of the cell, and that read is correct in
///         each condition. A transaction that fails during propagation ran no
///         last action, thus each cell holds its old value. A transaction that
///         fails after the last actions ran does not change them. Thus, each cell
///         holds its new value, and the read gives the carried value. A
///         transaction that completes never sets <see cref="Failed" />, and then
///         the carried value is the value of the cell.
///     </para>
///     <para>
///         A transaction sets <see cref="Failed" /> at the start of its failure
///         path, before it runs a release or a discard action. Thus, an action
///         that one of those gives to a different thread reads the correct answer,
///         when the transaction sent it through
///         <see cref="TransactionInternal.AfterLast" />.
///     </para>
/// </remarks>
internal sealed class TransactionOutcome
{
    private int failed;

    /// <summary>
    ///     Whether the transaction failed, thus a value carried from it can be
    ///     stale.
    /// </summary>
    internal bool Failed => Volatile.Read(ref this.failed) != 0;

    internal void MarkFailed() => Volatile.Write(location: ref this.failed, value: 1);
}
