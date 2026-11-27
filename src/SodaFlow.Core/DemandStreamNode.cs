namespace SodaFlow;

/// <summary>
///     The node of a stream that links to its inputs only while it has a
///     listener.
/// </summary>
/// <remarks>
///     <para>
///         Each attached listener of this node must be the link of an input, and the
///         action of that link must be able to link again. A merge is such a stream.
///     </para>
///     <para>
///         The unlink is lazy. An unlisten does not unlink. The node unlinks when an
///         input fires and the node has no listener, at the end of that transaction.
///         Thus, a listener that goes and comes back before an input fires finds the
///         links that it left. The two then cost the same as with no unlink. A node
///         that nothing observes works one more time, and then stops.
///     </para>
///     <para>
///         The unlink occurs in the last phase of the transaction. Thus, a listener
///         that comes back in the same transaction finds the links that it left. A
///         replay then cannot give the firing of an input to the stream two times. An
///         unlink in the last phase also unlinks each node of this type below it
///         that loses its last listener. Thus, a chain unlinks in one transaction.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the values of the stream.</typeparam>
internal sealed class DemandStreamNode<T> : StreamNode<T>, ILastAction
{
    private bool unlinkQueued;

    internal DemandStreamNode() => this.UnlinksOnDemand = true;

    /// <summary>True when this node has no link to its inputs.</summary>
    internal bool IsUnlinked { get; private set; }

    void ILastAction.Commit() => this.UnlinkIfUnused();

    // A transaction that failed did not unlink. The next firing with no listener tries again.
    void ILastAction.Discard() => this.unlinkQueued = false;

    private protected override void FiredWithNoListener(TransactionInternal trans)
    {
        if (this.IsUnlinked || this.unlinkQueued)
        {
            return;
        }

        this.unlinkQueued = true;
        trans.Last(this);
    }

    private protected override void FirstListenerLinked(TransactionInternal trans)
    {
        if (!this.IsUnlinked)
        {
            return;
        }

        // The owner reads its value from the inputs before the links come back, because the node is
        // unlinked until the next statement. The node finds the owner through the action of its first
        // link, which it holds weakly. A strong reference kept each derived behavior alive through
        // the link from its source.
        if (this.FirstAttached is Node.Target { ReachableAction: IDemandOwner owner })
        {
            owner.Relinking(trans);
        }

        this.IsUnlinked = false;
        this.FirstAttached = Relink(trans: trans, view: this.FirstAttached);

        if (this.MoreAttached != null)
        {
            for (int index = 0; index < this.MoreAttached.Count; index++)
            {
                // ReSharper disable once NullableWarningSuppressionIsUsed - The list holds no null, and a link again gives a link.
                this.MoreAttached[index] = Relink(trans: trans, view: this.MoreAttached[index])!;
            }
        }
    }

    // Only an unlink above this node, in the last phase on this thread, unlinks it here. An
    // unlisten by a caller keeps the links until an input fires.
    private protected override void LastListenerRemoved()
    {
        if (DemandUnlinks.Unlinking is { } trans && TransactionInternal.GetCurrentTransaction() == trans)
        {
            this.UnlinkIfUnused();
        }
    }

    private static IListenerWithWeakReference? Relink(TransactionInternal trans, IListenerWithWeakReference? view) =>
        view is Node.Target { ReachableAction: IRelinkable action } ? action.Relink(trans) : view;

    private void UnlinkIfUnused()
    {
        this.unlinkQueued = false;

        if (this.HasListeners || this.IsUnlinked)
        {
            return;
        }

        this.IsUnlinked = true;
        TransactionInternal? outer = DemandUnlinks.Unlinking;
        TransactionInternal trans = TransactionInternal.CurrentTransaction();
        DemandUnlinks.Unlinking = trans;

        try
        {
            this.FirstAttached?.Unlisten();

            if (this.MoreAttached != null)
            {
                foreach (IListenerWithWeakReference l in this.MoreAttached)
                {
                    l.Unlisten();
                }
            }
        }
        finally
        {
            DemandUnlinks.Unlinking = outer;
        }

        if (this.FirstAttached is Node.Target { ReachableAction: IDemandOwner owner })
        {
            owner.Unlinked(trans);
        }
    }
}

/// <summary>
///     The owner of a <see cref="DemandStreamNode{T}" /> whose stored value becomes
///     stale while the node is unlinked. A derived behavior is such an owner.
/// </summary>
internal interface IDemandOwner
{
    /// <summary>
    ///     The node links again. The owner reads its value from its inputs and
    ///     keeps it, because the links give it only the changes after this. A
    ///     switch of streams selects again.
    /// </summary>
    void Relinking(TransactionInternal trans);

    /// <summary>
    ///     The node unlinked in this transaction. A derived behavior keeps its
    ///     committed value for the first read after this.
    /// </summary>
    void Unlinked(TransactionInternal trans);
}

/// <summary>The state of an unlink across the nodes of a chain.</summary>
file static class DemandUnlinks
{
    /// <summary>
    ///     The transaction that unlinks a <see cref="DemandStreamNode{T}" /> now, or
    ///     null. Only the thread that holds the transaction lock writes it. A
    ///     different thread can read it, and does not find its own transaction there.
    /// </summary>
    /// <remarks>
    ///     A static field and not a field of the transaction, because a field of
    ///     the transaction cost each transaction 8 bytes.
    /// </remarks>
    internal static TransactionInternal? Unlinking;
}
