namespace SodaFlow;

/// <summary>
///     Queues one action at a node for a transaction, whatever the number of
///     inputs that call it. Each input listens into <see cref="Node" /> and calls
///     <see cref="Arrived" />. The first call of a transaction queues
///     <see cref="Run" />, and each subsequent call does nothing. The action runs
///     after all the inputs of the transaction, because the node ranks above each
///     of them.
/// </summary>
/// <remarks>
///     <para>
///         Before, an operator that reads all of its inputs before it sends had a
///         pulse stream. Each input sent into it, and a coalesce operation on that
///         stream made the one firing. That gave the same sequence through two
///         nodes more and two listener hops more. A transaction that fails
///         discards the queued action. Then the gate opens again, and
///         <see cref="Discard" /> runs.
///     </para>
///     <para>
///         The operator is the gate: it derives from this class and gives the two
///         steps as overrides. Before, the gate was an object of its own. It held
///         the two steps as delegates, and it wrapped each in a closure that
///         cleared the flag. Thus, a gate was about 280 bytes of objects that
///         held no state of the operator.
///     </para>
/// </remarks>
internal abstract class OnceGate
{
    private bool queued;

    /// <summary>The node into which each input links. It is the node of the output.</summary>
    internal abstract Node Node { get; }

    internal void Arrived(TransactionInternal trans)
    {
        if (this.queued)
        {
            return;
        }

        this.queued = true;
        trans.Prioritized(new GateEntry(this));
    }

    /// <summary>Reads the inputs of the transaction and sends.</summary>
    private protected abstract void Run(TransactionInternal trans);

    /// <summary>Clears what the inputs left when their transaction fails.</summary>
    private protected virtual void Discard()
    {
    }

    // The gate opens before each step, thus an input of a subsequent transaction can queue it again.
    private sealed class GateEntry(in OnceGate gate) : TransactionInternal.Entry(gate.Node)
    {
        private readonly OnceGate gate = gate;

        public override void Execute(TransactionInternal trans)
        {
            this.gate.queued = false;
            this.gate.Run(trans);
        }

        public override void Discard(System.Exception cause)
        {
            this.gate.queued = false;
            this.gate.Discard();
        }
    }
}
