using System;

namespace SodaFlow;

/// <summary>
///     The merge of two streams of different types through one node. It holds
///     the output stream and the two slots, and it is the gate through which each
///     input sends.
/// </summary>
/// <remarks>
///     The merge of maybes for n inputs captures each input in an object of its
///     own. It also makes an array of them, a closure that makes the array, and a
///     closure that reads them. A merge of two is the usual case, and it paid for
///     that generality: 1,559 bytes against 1,258 for <see cref="MergeTwo{T}" />.
///     This object holds the two slots in fields, and it is the gate. The
///     semantics are those of the merge of maybes. Each slot keeps
///     the last value that its input sent in the transaction. The gate reads the
///     two slots one time at the end of the sends. A slot knows the transaction
///     that filled it, thus a slot that a transaction filled and then discarded
///     gives no value.
/// </remarks>
/// <typeparam name="T1">The type of the values of the first stream.</typeparam>
/// <typeparam name="T2">The type of the values of the second stream.</typeparam>
/// <typeparam name="TResult">The type of the values of the output stream.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class MergeTwoMaybes<T1, T2, TResult> : OnceGate
{
    private readonly Func<MaybeInternal<T1>, MaybeInternal<T2>, TResult> f;
    private readonly Stream<TResult> @out;
    private CaptureSlot<T1> left;
    private CaptureSlot<T2> right;

    private MergeTwoMaybes(
        IKeepListenersAlive keepListenersAlive,
        Func<MaybeInternal<T1>, MaybeInternal<T2>, TResult> f)
    {
        this.f = f;
        this.@out = new Stream<TResult>(keepListenersAlive);
    }

    internal override Node Node => this.@out.Node;

    /// <summary>
    ///     Links <paramref name="first" /> and <paramref name="second" /> into a new
    ///     output stream and gives that stream.
    /// </summary>
    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> first,
        Stream<T2> second,
        Func<MaybeInternal<T1>, MaybeInternal<T2>, TResult> f)
    {
        MergeTwoMaybes<T1, T2, TResult> merge =
            new(
                keepListenersAlive: KeepListenersAliveInternal.Combine(
                    first: first.KeepListenersAlive,
                    second: second.KeepListenersAlive),
                f: f);

        IListener l1 =
            first.Listen(target: merge.Node, trans: trans, action: new Left(merge), suppressEarlierFirings: false);

        IListener l2 =
            second.Listen(
                target: merge.Node,
                trans: trans,
                action: new Right(merge),
                suppressEarlierFirings: false);

        return merge.@out.UnsafeAttachListener(l1).UnsafeAttachListener(l2);
    }

    // The actions of the two inputs. See MergeTwo.Input.
    private sealed class Left(in MergeTwoMaybes<T1, T2, TResult> merge) : StreamAction<T1>
    {
        private readonly MergeTwoMaybes<T1, T2, TResult> merge = merge;

        public override void Fire(TransactionInternal trans, T1 value) => this.merge.SetLeft(trans: trans, a: value);
    }

    private sealed class Right(in MergeTwoMaybes<T1, T2, TResult> merge) : StreamAction<T2>
    {
        private readonly MergeTwoMaybes<T1, T2, TResult> merge = merge;

        public override void Fire(TransactionInternal trans, T2 value) => this.merge.SetRight(trans: trans, a: value);
    }

    private void SetLeft(TransactionInternal trans, T1 a)
    {
        this.left.Set(trans: trans, value: a);
        this.Arrived(trans);
    }

    private void SetRight(TransactionInternal trans, T2 a)
    {
        this.right.Set(trans: trans, value: a);
        this.Arrived(trans);
    }

    private protected override void Run(TransactionInternal trans) =>
        this.@out.Send(trans: trans, a: this.f(arg1: this.left.Take(trans), arg2: this.right.Take(trans)));
}
