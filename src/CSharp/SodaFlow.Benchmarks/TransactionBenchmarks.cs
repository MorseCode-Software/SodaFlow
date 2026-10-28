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

    private readonly StreamSink<int> sink = Stream.CreateSink<int>();
    private readonly StreamSink<int> chainSource = Stream.CreateSink<int>();

    private IStrongListener? listener;
    private IStrongListener? chainListener;

    /// <summary>Gives the sink one listener, and builds the chain of maps with one listener at its end.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.listener =
            this.sink.ListenStrong(static _ =>
            {
            });

        Stream<int> chain = this.chainSource;

        for (int i = 0; i < ChainLength; i++)
        {
            chain = chain.Map(static v => v + 1);
        }

        this.chainListener =
            chain.ListenStrong(static _ =>
            {
            });
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.listener?.Unlisten();
        this.chainListener?.Unlisten();
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
}
