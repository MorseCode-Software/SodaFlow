using System;

namespace SodaFlow;

/// <summary>
///     The output of a lift while its inputs link into it. It holds the output
///     stream, the gate through which each input sends, and the step that puts
///     the inputs together.
/// </summary>
/// <remarks>
///     The gate makes one firing for a transaction that updates more than one
///     input. Each input captures its new value as that value moves through the
///     graph. The step that puts the inputs together reads the captures, which is
///     why it gets the transaction. Before, a static method took the step, the
///     initial value, and a factory of the listeners as three delegates, and the
///     factory made an array. This object replaces all of that with one
///     allocation, and its Send method is the action of the gate.
/// </remarks>
/// <typeparam name="TResult">The type of the lifted value.</typeparam>
internal sealed class LiftOutput<TResult>
{
    private readonly OnceGate gate;
    private readonly Stream<TResult> @out;
    private readonly Func<TransactionInternal, TResult> recombine;

    internal LiftOutput(IKeepListenersAlive keepListenersAlive, Func<TransactionInternal, TResult> recombine)
    {
        this.@out = new Stream<TResult>(keepListenersAlive);
        this.recombine = recombine;
        this.gate = new OnceGate(node: this.@out.Node, action: this.Send);
    }

    /// <summary>Links one input into the output through the gate.</summary>
    internal void Capture<TInput>(Stream<TInput> input, TransactionInternal trans, InputCapture<TInput> capture) =>
        this.@out.UnsafeAttachListener(
            StreamInternal.Capture(input: input, gate: this.gate, trans: trans, capture: capture));

    /// <summary>Holds the output, with an initial value that the first sample computes.</summary>
    internal Behavior<TResult> Hold(TransactionInternal trans, Func<TransactionInternal, TResult> initialValue) =>
        this.@out.HoldLazyInternal(trans: trans, initialValue: new LazySample<TResult>(initialValue));

    private void Send(TransactionInternal trans) => this.@out.Send(trans: trans, a: this.recombine(trans));
}
