using System.Threading;

namespace SodaFlow;

/// <summary>
///     The outcome of one transaction, for code that runs after the transaction on a
///     different thread.
/// </summary>
/// <remarks>
///     A transaction sets <see cref="Failed" /> at the start of its failure path,
///     before it runs a release or a discard action. Thus, an action that one of
///     those gives to a different thread reads the correct answer, when the
///     transaction sent it through <see cref="TransactionInternal.AfterLast" />. A
///     transaction that completes never sets it.
/// </remarks>
internal sealed class TransactionOutcome
{
    private int failed;

    /// <summary>Whether the transaction failed.</summary>
    internal bool Failed => Volatile.Read(ref this.failed) != 0;

    internal void MarkFailed() => Volatile.Write(location: ref this.failed, value: 1);
}
