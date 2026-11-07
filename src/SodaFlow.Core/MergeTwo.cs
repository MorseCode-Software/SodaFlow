using System;

namespace SodaFlow;

/// <summary>
///     The merge of two streams through one node. It holds the output stream and
///     the two slots, and it is the gate through which each input sends.
/// </summary>
/// <remarks>
///     The list merge does the same for n inputs. It has an array of slots, and
///     each input has an object that holds its index. A merge of two is the usual
///     case, and it paid for that generality: 1,562 bytes against 425 for a map.
///     This object holds the two slots in fields, and it is the gate. The
///     semantics are those of the list merge. The left value goes to the left of
///     <c>f</c> when the two fire. An input that fires more than one time in a
///     transaction folds into its own slot, in the sequence of its firings. A
///     transaction that fails clears the slots.
/// </remarks>
/// <typeparam name="T">The type of the values of the streams.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class MergeTwo<T> : OnceGate
{
    private readonly Func<T, T, T> f;
    private readonly Stream<T> @out;
    private MaybeInternal<T> left;
    private MaybeInternal<T> right;

    private MergeTwo(IKeepListenersAlive keepListenersAlive, Func<T, T, T> f)
    {
        this.f = f;
        this.@out = new Stream<T>(keepListenersAlive);
    }

    internal override Node Node => this.@out.Node;

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

        IListenerInternal l1 =
            first.Listen(
                target: merge.Node,
                trans: trans,
                action: new Input(merge: merge, left: true),
                suppressEarlierFirings: false);

        IListenerInternal l2 =
            second.Listen(
                target: merge.Node,
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
        this.Arrived(trans);
    }

    private void SetRight(TransactionInternal trans, T a)
    {
        this.right = this.Fold(slot: this.right, a: a);
        this.Arrived(trans);
    }

    private MaybeInternal<T> Fold(MaybeInternal<T> slot, T a) =>
        slot.TryGetValue(out T earlier) ? MaybeInternal.Some(this.f(arg1: earlier, arg2: a)) : MaybeInternal.Some(a);

    private protected override void Run(TransactionInternal trans)
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

    private protected override void Discard()
    {
        this.left = MaybeInternal<T>.None;
        this.right = MaybeInternal<T>.None;
    }
}
