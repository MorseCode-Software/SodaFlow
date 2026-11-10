using System;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public sealed class TransactionTests
{
    [Test]
    public async Task PostSeeOutside()
    {
        OperationCanceledException? actual = null;
        AutoResetEvent re = new(false);

        using (CancellationTokenSource cts = new())
        {
            Task task =
                // ReSharper disable once MethodSupportsCancellation - We want to observe cancellation within the
                // running task.
                Task.Run(() =>
                {
                    Transaction.Post(() =>
                    {
                        re.Set();

                        Thread.Sleep(5000);

                        // ReSharper disable once AccessToDisposedClosure - Disposable will happen after this is
                        // reached.
                        cts.Token.ThrowIfCancellationRequested();
                    });
                });

            re.WaitOne();

            // ReSharper disable once MethodHasAsyncOverload - CancelAsync arrived in .NET 8,
            // and this compiles for net472 as well.
            cts.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException e)
            {
                actual = e;
            }
        }

        await Assert.That(actual).IsNotNull();
    }

    // The root commits before it runs its posted actions. A posted action that throws made the root
    // discard itself. Its outcome then showed a failure, although each value that it sent stayed in
    // the graph.
    [Test]
    public async Task ARootKeepsItsCommittedOutcomeWhenAPostedActionThrows()
    {
        TransactionOutcome? outcome = null;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    outcome = TransactionInternal.CurrentOutcome();
                    Transaction.Post(static () => throw new InvalidOperationException("posted"));
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(outcome?.Committed).IsTrue();
    }

    // A defer is a split of one item, and the limit counts the rounds of splits. This chain counts
    // from zero, and the round with the number n sends n - 1. The filter stops the value that is
    // one less than the limit, which the round of the limit sends. Thus, the chain ends in the
    // last round that can run.
    [Test]
    public async Task AChainOfDefersThatEndsInTheLastRoundReturns()
    {
        const int last = TransactionInternal.DeferredChainLimit - 1;

        Cell<int> count = CountThroughADeferTo(last);

        await Assert.That(count.Sample()).IsEqualTo(last);
    }

    // The same chain with one more step makes one more round than the limit.
    [Test]
    public async Task AChainOfDefersThatNeedsOneMoreRoundThrows() =>
        await Assert.That(static () =>
            {
                CountThroughADeferTo(TransactionInternal.DeferredChainLimit);
            })
            .ThrowsExactly<DeferredChainLimitException>();

    // The limit counts the generations of posts also. The first action is the first generation,
    // and each action posts the next one. Thus, a chain of as many actions as the limit ends in
    // the last generation that can run.
    [Test]
    public async Task AChainOfPostsThatEndsInTheLastGenerationReturns()
    {
        int ran = 0;

        Transaction.RunVoid(() => PostAChain(actions: TransactionInternal.DeferredChainLimit, onRun: () => ran++));

        await Assert.That(ran).IsEqualTo(TransactionInternal.DeferredChainLimit);
    }

    // The same chain with one more action makes one more generation than the limit.
    [Test]
    public async Task AChainOfPostsThatNeedsOneMoreGenerationThrows() =>
        await Assert.That(static () =>
                Transaction.RunVoid(static () =>
                    PostAChain(actions: TransactionInternal.DeferredChainLimit + 1, onRun: static () => { })))
            .ThrowsExactly<DeferredChainLimitException>();

    // Each step of a chain of defers posts a chain of actions. The count of generations starts
    // again after each round. One count for the two kinds is the product of the two depths, and
    // here that product is more than the limit.
    [Test]
    public async Task AChainOfPostsInEachStepOfAChainOfDefersIsNotTheProduct()
    {
        int depth = (int)Math.Sqrt(TransactionInternal.DeferredChainLimit) + 1;
        StreamSink<int> start = Stream.CreateSink<int>();
        int ran = 0;

        // Each value above zero comes back one less, in a transaction of its own.
        Stream<int> all =
            Stream.Loop<int>()
                .WithoutCaptures(back =>
                    Operational.Defer(start.OrElse(back).Filter(static v => v > 0).Map(static v => v - 1)));

        using IStrongListener l = all.ListenStrong(_ => PostAChain(actions: depth, onRun: () => ran++));

        start.Send(depth);

        await Assert.That(depth * (depth + 1)).IsGreaterThan(TransactionInternal.DeferredChainLimit);
        await Assert.That(ran).IsEqualTo(depth * depth);
    }

    // The limit is on the depth of the chain, and not on the number of deferred transactions. One
    // split of a list that has more items than the limit is one round.
    [Test]
    public async Task ASplitOfMoreItemsThanTheLimitIsOneRound()
    {
        const int count = TransactionInternal.DeferredChainLimit + 1;
        StreamSink<int[]> sink = Stream.CreateSink<int[]>();
        int seen = 0;

        using IStrongListener l = Operational.Split<int, int[]>(sink).ListenStrong(_ => seen++);

        sink.Send(new int[count]);

        await Assert.That(seen).IsEqualTo(count);
    }

    // The count starts again for each transaction. Two transactions that each run more than half
    // of the limit do not come to it.
    [Test]
    public async Task TheLimitIsForTheDeferredWorkOfOneTransaction()
    {
        const int steps = TransactionInternal.DeferredChainLimit * 3 / 5;
        StreamSink<int> start = Stream.CreateSink<int>();
        int seen = 0;

        // Each value above zero comes back one less, in a transaction of its own.
        Stream<int> all =
            Stream.Loop<int>()
                .WithoutCaptures(back =>
                    Operational.Defer(start.OrElse(back).Filter(static v => v > 0).Map(static v => v - 1)));

        using IStrongListener l = all.ListenStrong(_ => seen++);

        start.Send(steps);
        start.Send(steps);

        await Assert.That(seen).IsEqualTo(2 * steps);
    }

    // The chain has no end. The round with the number n sends n, and the last round that runs is
    // the round of the limit. Thus, the cell holds the limit after the exception.
    [Test]
    public async Task TheValueOfTheLastRoundBeforeTheLimitStays()
    {
        StreamSink<int> start = Stream.CreateSink<int>();

        (_, Cell<int> steps) =
            Stream.Loop<int>()
                .WithCaptures(back =>
                {
                    Stream<int> all = start.OrElse(back);

                    return (Stream: Operational.Defer(all.Map(static v => v + 1)), Captures: all.Hold(0));
                });

        await Assert.That(() => start.Send(0)).ThrowsExactly<DeferredChainLimitException>();

        await Assert.That(steps.Sample()).IsEqualTo(TransactionInternal.DeferredChainLimit);
    }

    // Posts the first action of a chain, in the current transaction. Each action posts the next
    // one, until the given number of actions ran.
    private static void PostAChain(int actions, Action onRun)
    {
        Transaction.Post(() => Next(actions - 1));

        return;

        void Next(int more)
        {
            onRun();

            if (more > 0)
            {
                Transaction.Post(() => Next(more - 1));
            }
        }
    }

    private static Cell<int> CountThroughADeferTo(int last) =>
        Cell.Loop<int>()
            .WithoutCaptures(l =>
                Operational.Defer(Operational.Value(l)).Filter(v => v < last).Map(static v => v + 1).Hold(0));
}
