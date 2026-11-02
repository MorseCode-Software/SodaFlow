using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a transaction costs when its first entry in the priority queue has a
///     deep rank.
/// </summary>
/// <remarks>
///     <para>
///         The priority queue starts each transaction at rank zero and walks up to
///         the first non-empty bucket. A send to a sink with one listener queues at
///         rank one, thus the walk is one bucket. A merge links its second source
///         to its output node, whose rank is above the rank of each source. Thus, a
///         send to a sink that is the second source of a merge with a deep stream
///         queues at that deep rank. Nothing lower is in the queue, and the walk is
///         that many buckets. The difference between the two benchmarks at each
///         length is the walk, plus the merge itself, which does not change with
///         the length.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class DeepFirstEntryBenchmarks
{
    private readonly StreamSink<int> sink = Stream.CreateSink<int>();
    private readonly StreamSink<int> deepSink = Stream.CreateSink<int>();

    private IStrongListener? listener;
    private IStrongListener? deepListener;

    /// <summary>
    ///     The number of maps in the chain of streams that the merge reads.
    /// </summary>
    [Params(10, 100, 1000)]
    public int Length { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Gives the first sink one listener. Merges a chain of streams, which
    ///     never fires, with the second sink, and gives the merge one listener.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.listener =
            this.sink.ListenStrong(static _ =>
            {
            });

        this.deepListener =
            Transaction.Run(() =>
            {
                Stream<int> chain = Stream.CreateSink<int>();

                for (int i = 0; i < this.Length; i++)
                {
                    chain = chain.Map(static v => v + 1);
                }

                return chain.OrElse(this.deepSink)
                    .ListenStrong(static _ =>
                    {
                    });
            });
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.listener?.Unlisten();
        this.deepListener?.Unlisten();
    }

    /// <summary>Sends one value to the sink with one listener, with no transaction open.</summary>
    [Benchmark(Baseline = true, Description = "send to a sink with one listener")]
    public void Send() => this.sink.Send(1);

    /// <summary>
    ///     Sends one value to the sink that is the second source of the merge with
    ///     the deep stream, with no transaction open.
    /// </summary>
    [Benchmark(Description = "send to a sink merged after a deep stream")]
    public void SendDeep() => this.deepSink.Send(1);
}
