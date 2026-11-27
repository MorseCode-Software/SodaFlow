using System;
using System.Linq;
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

        using IListener l = all.Listen(_ => PostAChain(actions: depth, onRun: () => ran++));

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

        using IListener l = Operational.Split<int, int[]>(sink).Listen(_ => seen++);

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

        using IListener l = all.Listen(_ => seen++);

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

    // The exception has the three constructors that an exception of the platform has.
    [Test]
    public async Task TheExceptionOfTheLimitHasTheUsualConstructors()
    {
        InvalidOperationException inner = new("inner");

        await Assert.That(new DeferredChainLimitException().Message)
            .IsEqualTo(DeferredChainLimitException.DefaultMessage);

        await Assert.That(new DeferredChainLimitException(message: "given").Message).IsEqualTo("given");

        await Assert.That(new DeferredChainLimitException(message: "given", innerException: inner).InnerException)
            .IsSameReferenceAs(inner);
    }

    // A chain of releases as long as the depth limit runs each release. Its innermost exception is
    // the throw of the last action, and not the limit.
    [Test]
    public async Task AChainOfReleasesAsLongAsTheDepthLimitRunsEachRelease()
    {
        (Exception thrown, int actions, int releases) = RunAReleaseChain(TransactionInternal.AfterReleaseDepthLimit);

        await Assert.That(actions).IsEqualTo(TransactionInternal.AfterReleaseDepthLimit);
        await Assert.That(releases).IsEqualTo(TransactionInternal.AfterReleaseDepthLimit);
        await Assert.That(Innermost(thrown) is DeferredChainLimitException).IsFalse();
        await Assert.That(Innermost(thrown).Message).IsEqualTo("The action fails.");
    }

    // One more release in the chain comes to the limit. The post of that release throws, and it does
    // not run its action or its release. Thus, a chain that is longer, or that has no end, stops
    // there, and the process does not run out of stack.
    [Test]
    public async Task AChainOfReleasesStopsAtTheDepthLimit()
    {
        (Exception thrown, int actions, int releases) = RunAReleaseChain(100);
        Exception innermost = Innermost(thrown);

        await Assert.That(actions).IsEqualTo(TransactionInternal.AfterReleaseDepthLimit);
        await Assert.That(releases).IsEqualTo(TransactionInternal.AfterReleaseDepthLimit);
        await Assert.That(innermost).IsTypeOf<DeferredChainLimitException>();

        await Assert.That(innermost.Message)
            .IsEqualTo(DeferredChainLimitException.MessageForDepth(TransactionInternal.AfterReleaseDepthLimit));
    }

    // Each new transaction at the limit throws, not only a post. A release can catch the throw.
    [Test]
    public async Task ATransactionInAReleaseAtTheDepthLimitThrows()
    {
        int releases = 0;
        Exception? atTheLimit = null;

        try
        {
            Transaction.Post(action: Fail, onFailure: Release);
        }
        catch (Exception)
        {
            // The chain throws an AggregateException of the throws of its actions. The test reads
            // the counts and not that exception.
        }

        await Assert.That(releases).IsEqualTo(TransactionInternal.AfterReleaseDepthLimit);
        await Assert.That(atTheLimit).IsTypeOf<DeferredChainLimitException>();

        return;

        void Release(Exception _)
        {
            releases++;

            if (releases < TransactionInternal.AfterReleaseDepthLimit)
            {
                Transaction.Post(action: Fail, onFailure: Release);

                return;
            }

            try
            {
                Transaction.RunVoid(static () => { });
            }
            catch (DeferredChainLimitException e)
            {
                atTheLimit = e;
            }
        }
    }

    // The depth goes back down after a chain that stopped at the limit. Thus, a second chain as
    // long as the limit runs each of its releases on the same thread.
    [Test]
    public async Task TheDepthGoesBackDownAfterAChainThatStoppedAtTheLimit()
    {
        _ = RunAReleaseChain(100);

        (Exception thrown, int _, int releases) = RunAReleaseChain(TransactionInternal.AfterReleaseDepthLimit);

        await Assert.That(releases).IsEqualTo(TransactionInternal.AfterReleaseDepthLimit);
        await Assert.That(Innermost(thrown) is DeferredChainLimitException).IsFalse();
        await Assert.That(Innermost(thrown).Message).IsEqualTo("The action fails.");
    }

    // The depth is for one thread. A release at the limit waits, and a transaction on a different
    // thread then runs a release that opens a transaction of its own.
    [Test]
    public async Task TheDepthLimitOfOneThreadDoesNotStopADifferentThread()
    {
        TaskCompletionSource<bool> atTheLimit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> otherDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int releases = 0;
        bool otherReleaseRan = false;
        Exception? otherFailure = null;

        Task deep = Task.Run(() =>
        {
            try
            {
                Transaction.Post(action: Fail, onFailure: Release);
            }
            catch (Exception)
            {
                // The chain throws an AggregateException of the throws of its actions. The test
                // reads the other thread and not that exception.
            }
        });

        bool reached =
            await Task.WhenAny(tasks: [atTheLimit.Task, Task.Delay(delay: TimeSpan.FromSeconds(30))]) == atTheLimit.Task;

        try
        {
            Transaction.Post(
                action: Fail,
                onFailure: _ =>
                {
                    Transaction.RunVoid(static () => { });
                    otherReleaseRan = true;
                });
        }
        catch (InvalidOperationException e) when (e is not DeferredChainLimitException)
        {
            // The action fails on purpose. The release then runs, and its transaction must start.
        }
        catch (Exception e)
        {
            otherFailure = e;
        }

        otherDone.SetResult(true);
        await deep;

        await Assert.That(reached).IsTrue();
        await Assert.That(otherFailure).IsNull();
        await Assert.That(otherReleaseRan).IsTrue();

        return;

        void Release(Exception _)
        {
            releases++;

            if (releases < TransactionInternal.AfterReleaseDepthLimit)
            {
                Transaction.Post(action: Fail, onFailure: Release);

                return;
            }

            atTheLimit.SetResult(true);
            otherDone.Task.Wait(TimeSpan.FromSeconds(30));
        }
    }

    // A release posts an action that throws, with a release that does the same, until the chain
    // has the given number of releases. Gives what the first post threw, and the counts. The first
    // action throws, thus the first post always throws.
    private static (Exception Thrown, int Actions, int Releases) RunAReleaseChain(int length)
    {
        int actions = 0;
        int releases = 0;

        try
        {
            Transaction.Post(action: CountAndFail, onFailure: Release);
        }
        catch (Exception e)
        {
            return (Thrown: e, Actions: actions, Releases: releases);
        }

        throw new InvalidOperationException("A chain of releases did not throw.");

        void CountAndFail()
        {
            actions++;
            Fail();
        }

        void Release(Exception _)
        {
            releases++;

            if (releases < length)
            {
                Transaction.Post(action: CountAndFail, onFailure: Release);
            }
        }
    }

    private static void Fail() => throw new InvalidOperationException("The action fails.");

    // The last exception at each level, from the top level to the bottom level.
    private static Exception Innermost(Exception e)
    {
        while (e is AggregateException { InnerExceptions.Count: > 0 } aggregate)
        {
            e = aggregate.InnerExceptions.Last();
        }

        return e;
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
