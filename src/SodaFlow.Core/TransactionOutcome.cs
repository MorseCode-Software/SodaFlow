using System.Threading;

namespace SodaFlow;

/// <summary>
///     The outcome of one transaction, for code that runs after the transaction on a
///     different thread.
/// </summary>
/// <remarks>
///     <para>
///         The outcome has three states. It is unknown until the transaction ran
///         each last action or started its failure path. A transaction sets
///         <see cref="Committed" /> after its last actions ran, before it runs an
///         after-last action. It sets <see cref="Failed" /> at the start of its
///         failure path, before it runs a release or a discard action. A failure
///         after the last actions ran replaces <see cref="Committed" /> with
///         <see cref="Failed" />.
///     </para>
///     <para>
///         Code that carries a value from the transaction uses that value only when
///         the outcome is <see cref="Committed" />. In each other state it reads the
///         cell, and that read is correct in each condition. A read while the
///         outcome is unknown opens a transaction, thus it waits until the
///         transaction that made the value ends. A transaction that failed during
///         propagation ran no last action, thus each cell holds its old value. A
///         transaction that failed after the last actions ran does not change them.
///         Thus, each cell holds its new value, and the read gives the carried value.
///     </para>
///     <para>
///         The unknown state is important where a scheduler gives an action to the
///         binding thread before the transaction ends. The schedulers in
///         SodaFlow.Core.Bindable.ObjectModel send after the outcome is known,
///         through <see cref="TransactionInternal.AfterLast" />. A scheduler that a
///         consumer writes can run the action immediately on a different thread,
///         and the read keeps the value correct there also.
///     </para>
/// </remarks>
internal sealed class TransactionOutcome
{
    private const int UnknownState = 0;
    private const int CommittedState = 1;
    private const int FailedState = 2;

    private int state;

    /// <summary>Whether the transaction set its outcome.</summary>
    internal bool Known => Volatile.Read(ref this.state) != UnknownState;

    /// <summary>
    ///     Whether the transaction ran each last action and did not fail. A value
    ///     carried from the transaction is then the value of its cell.
    /// </summary>
    internal bool Committed => Volatile.Read(ref this.state) == CommittedState;

    /// <summary>
    ///     Whether the transaction failed, thus a value carried from it can be
    ///     stale.
    /// </summary>
    internal bool Failed => Volatile.Read(ref this.state) == FailedState;

    internal void MarkCommitted() => Volatile.Write(location: ref this.state, value: CommittedState);

    internal void MarkFailed() => Volatile.Write(location: ref this.state, value: FailedState);
}
