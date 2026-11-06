using System;

namespace SodaFlow;

/// <summary>
///     The merge of two streams through one node. It holds the output stream, the
///     gate through which each input sends, and the two slots. Its methods are the
///     actions of the inputs and of the gate.
/// </summary>
/// <remarks>
///     The list merge does the same for n inputs. It has an array of slots, and
///     each input has a closure and a delegate, because each input captures its
///     index. A merge of two is the usual case, and it paid for that generality:
///     1,562 bytes against 425 for a map. This object holds the two slots in fields
///     and gives its methods as the delegates: 1,362 bytes. The second link, the
///     gate, and their delegates are the cost that remains. The semantics are those
///     of the list merge. The left value goes to the left of <c>f</c> when the two
///     fire. An input that fires more than one time in a transaction folds into its
///     own slot, in the sequence of its firings. A transaction that fails clears
///     the slots.
/// </remarks>
/// <typeparam name="T">The type of the values of the streams.</typeparam>
internal sealed class MergeTwo<T>
{
    private readonly Func<T, T, T> f;
    private readonly OnceGate gate;
    private readonly Stream<T> @out;
    private MaybeInternal<T> left;
    private MaybeInternal<T> right;

    private MergeTwo(IKeepListenersAlive keepListenersAlive, Func<T, T, T> f)
    {
        this.f = f;
        this.@out = new Stream<T>(keepListenersAlive);
        this.gate = new OnceGate(node: this.@out.Node, action: this.Combine, discard: this.Discard);
    }

    /// <summary>
    ///     Links <paramref name="first" /> and <paramref name="second" /> into a new
    ///     output stream and gives that stream.
    /// </summary>
    internal static Stream<T> Create(TransactionInternal trans, Stream<T> first, Stream<T> second, Func<T, T, T> f)
    {
        MergeTwo<T> merge =
            new(
                keepListenersAlive: KeepListenersAliveInternal.Combine(
                    first: first.KeepListenersAlive,
                    second: second.KeepListenersAlive),
                f: f);

        IListener l1 =
            first.Listen(
                target: merge.gate.Node,
                trans: trans,
                action: new Input(merge: merge, left: true),
                suppressEarlierFirings: false);

        IListener l2 =
            second.Listen(
                target: merge.gate.Node,
                trans: trans,
                action: new Input(merge: merge, left: false),
                suppressEarlierFirings: false);

        return merge.@out.UnsafeAttachListener(l1).UnsafeAttachListener(l2);
    }

    // The action of one input: one object for each side. Before, a delegate to a method here
    // was one delegate and a listener object for each side.
    private sealed class Input(in MergeTwo<T> merge, in bool left) : StreamAction<T>
    {
        private readonly bool left = left;
        private readonly MergeTwo<T> merge = merge;

        public override void Fire(TransactionInternal trans, T value)
        {
            if (this.left)
            {
                this.merge.SetLeft(trans: trans, a: value);
            }
            else
            {
                this.merge.SetRight(trans: trans, a: value);
            }
        }
    }

    private void SetLeft(TransactionInternal trans, T a)
    {
        this.left = this.Fold(slot: this.left, a: a);
        this.gate.Arrived(trans);
    }

    private void SetRight(TransactionInternal trans, T a)
    {
        this.right = this.Fold(slot: this.right, a: a);
        this.gate.Arrived(trans);
    }

    private MaybeInternal<T> Fold(MaybeInternal<T> slot, T a) =>
        slot.TryGetValue(out T earlier) ? MaybeInternal.Some(this.f(arg1: earlier, arg2: a)) : MaybeInternal.Some(a);

    private void Combine(TransactionInternal trans)
    {
        MaybeInternal<T> l = this.left;
        MaybeInternal<T> r = this.right;
        this.left = MaybeInternal<T>.None;
        this.right = MaybeInternal<T>.None;

        if (l.TryGetValue(out T leftValue))
        {
            this.@out.Send(
                trans: trans,
                a: r.TryGetValue(out T rightValue) ? this.f(arg1: leftValue, arg2: rightValue) : leftValue);
        }
        else if (r.TryGetValue(out T rightValue))
        {
            this.@out.Send(trans: trans, a: rightValue);
        }
    }

    private void Discard()
    {
        this.left = MaybeInternal<T>.None;
        this.right = MaybeInternal<T>.None;
    }
}
