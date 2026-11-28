using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a transaction costs when it does almost nothing.
/// </summary>
/// <remarks>
///     <para>
///         Each send that is not in a transaction opens one. Thus, the fixed cost
///         of a transaction is in each such send. The empty transaction is that
///         fixed cost, and the send adds one sink and one listener to it.
///     </para>
///     <para>
///         The send through the chain of maps queues one entry in the priority
///         queue for each map and one for the listener. Thus, it measures the cost
///         of one entry: the queue operation, and what the queue operation took
///         before it ran, such as a lock.
///     </para>
///     <para>
///         The last three cases send through the chain and post one action with a
///         release, in one transaction. The first of them commits: the posted action
///         runs after the commit. In the second, the body throws after the post. In
///         the third, the body also sends to a sink whose listener throws, and that
///         throw comes while the transaction propagates. A failed
///         transaction discards its sends, runs the release of the post, and gives
///         back none of its queues to the next transaction. Thus, the difference
///         between the three is the cost of the failure path, with the cost of the
///         throw in it.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class TransactionBenchmarks
{
    private const int ChainLength = 32;

    // One exception for each failed transaction, thus the cases do not measure its construction.
    private static readonly InvalidOperationException Failure = new("The benchmark fails this transaction.");

    private readonly StreamSink<int> sink = Stream.CreateSink<int>();
    private readonly StreamSink<int> chainSource = Stream.CreateSink<int>();
    private readonly StreamSink<int> failingSink = Stream.CreateSink<int>();

    private IListener? listener;
    private IListener? chainListener;
    private IListener? failingListener;

    /// <summary>Gives the sink one listener, and builds the chain of maps with one listener at its end.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.listener =
            this.sink.Listen(static _ =>
            {
            });

        Stream<int> chain = this.chainSource;

        for (int i = 0; i < ChainLength; i++)
        {
            chain = chain.Map(static v => v + 1);
        }

        this.chainListener =
            chain.Listen(static _ =>
            {
            });

        this.failingListener = this.failingSink.Listen(static _ => throw Failure);
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.listener?.Dispose();
        this.chainListener?.Dispose();
        this.failingListener?.Dispose();
    }

    /// <summary>Opens a transaction that does nothing, and closes it.</summary>
    [Benchmark(Baseline = true, Description = "empty transaction")]
    public void EmptyTransaction() =>
        Transaction.RunVoid(static () =>
        {
        });

    /// <summary>Sends one value to a sink that has one listener, with no transaction open.</summary>
    [Benchmark(Description = "send to a sink with one listener")]
    public void Send() => this.sink.Send(1);

    /// <summary>Sends one value through the chain of maps to its listener, with no transaction open.</summary>
    [Benchmark(Description = "send through a chain of maps")]
    public void SendThroughChain() => this.chainSource.Send(1);

    /// <summary>Sends through the chain and posts an action with a release, in one transaction that commits.</summary>
    [Benchmark(Description = "send and post, commit")]
    public void SendAndPostThenCommit() =>
        Transaction.RunVoid(() =>
        {
            this.chainSource.Send(1);
            PostWithARelease();
        });

    /// <summary>The same, where the body throws after the post.</summary>
    [Benchmark(Description = "send and post, the body throws")]
    public void SendAndPostThenTheBodyThrows()
    {
        try
        {
            Transaction.RunVoid(() =>
            {
                this.chainSource.Send(1);
                PostWithARelease();

                throw Failure;
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>The same, where a listener throws while the transaction propagates.</summary>
    [Benchmark(Description = "send and post, a listener throws")]
    public void SendAndPostThenAListenerThrows()
    {
        try
        {
            Transaction.RunVoid(() =>
            {
                this.chainSource.Send(1);
                PostWithARelease();
                this.failingSink.Send(1);
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void PostWithARelease() =>
        Transaction.Post(
            action: static () =>
            {
            },
            onFailure: static _ =>
            {
            });
}
