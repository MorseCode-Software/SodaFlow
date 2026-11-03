using System;

namespace SodaFlow;

/// <summary>
///     Queues one action at a node for a transaction, whatever the number of
///     inputs that call it. Each input listens into <see cref="Node" /> and calls
///     <see cref="Arrived" />. The first call of a transaction queues the action,
///     and each subsequent call does nothing. The action runs after all the inputs
///     of the transaction, because the node ranks above each of them.
/// </summary>
/// <remarks>
///     Before, an operator that reads all of its inputs before it sends had a
///     pulse stream. Each input sent into it, and a coalesce operation on that
///     stream made the one firing. That gave the same sequence through two nodes
///     more and two listener hops more. A transaction that fails discards the
///     queued action. Then the gate opens again, and the discard action of the
///     operator runs.
/// </remarks>
internal sealed class OnceGate
{
    private readonly Action<TransactionInternal> action;
    private readonly Action discard;
    private bool queued;

    internal OnceGate(Node node, Action<TransactionInternal> action)
        : this(node: node, action: action, discard: static () => { })
    {
    }

    internal OnceGate(Node node, Action<TransactionInternal> action, Action discard)
    {
        this.Node = node;

        // Each of these captures only this gate, thus each is one delegate for the life of the
        // gate and not one for each transaction.
        this.action =
            trans =>
            {
                this.queued = false;
                action(trans);
            };

        this.discard =
            () =>
            {
                this.queued = false;
                discard();
            };
    }

    internal Node Node { get; }

    internal void Arrived(TransactionInternal trans)
    {
        if (this.queued)
        {
            return;
        }

        this.queued = true;
        trans.Prioritized(node: this.Node, action: this.action, discard: this.discard);
    }
}
