using System;

namespace SodaFlow;

/// <summary>
///     One input of an operation that puts the firings of more than one input
///     together. It holds the slot for the captured value, and it is the action
///     that fills the slot and then calls the shared gate.
/// </summary>
/// <remarks>
///     Before, each input had a lambda that wrote its slot, and Capture wrapped
///     that lambda in a second one that called the gate. That was two closures
///     and two delegates for each input, and the slot sat in the closure of the
///     operation. This object holds the slot and the gate, and its Set method is
///     the action. Thus, each input costs one object and one delegate. The step
///     that puts the inputs together reads the slot through one of the Take
///     methods.
/// </remarks>
/// <typeparam name="T">The type of the captured value.</typeparam>
internal sealed class InputCapture<T>
{
    private OnceGate? gate;
    private CaptureSlot<T> slot;

    /// <summary>
    ///     Gives the action that fills the slot and calls <paramref name="gate" />.
    ///     An operation makes its gate after its inputs, thus the gate arrives here
    ///     and not in the constructor.
    /// </summary>
    internal Action<TransactionInternal, T> Arm(OnceGate gate)
    {
        this.gate = gate;

        return this.Set;
    }

    /// <summary>
    ///     Gives the value that <paramref name="trans" /> captured, or the current
    ///     value of <paramref name="input" />, and clears the slot. See
    ///     <see cref="Behavior{T}.Take{TInput}" />.
    /// </summary>
    internal T Take(Behavior<T> input, TransactionInternal trans) =>
        Behavior<T>.Take(pending: ref this.slot, input: input, trans: trans);

    /// <summary>
    ///     Gives the value that <paramref name="trans" /> captured, when the input
    ///     fired in it, and clears the slot. See <see cref="CaptureSlot{T}.Take" />.
    /// </summary>
    internal MaybeInternal<T> Take(TransactionInternal trans) => this.slot.Take(trans);

    private void Set(TransactionInternal trans, T value)
    {
        this.slot.Set(trans: trans, value: value);

        // ReSharper disable once NullableWarningSuppressionIsUsed - Arm set the gate before the input could fire,
        // because Capture arms this object and then links it.
        this.gate!.Arrived(trans);
    }
}
