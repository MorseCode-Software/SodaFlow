using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a transaction costs when it does almost nothing.
/// </summary>
/// <remarks>
///     Each send that is not in a transaction opens one. Thus, the fixed cost of a
///     transaction is in each such send. The empty transaction is that fixed cost,
///     and the send adds one sink and one listener to it.
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
    private readonly StreamSink<int> sink = Stream.CreateSink<int>();

    private IStrongListener? listener;

    /// <summary>Gives the sink one listener.</summary>
    [GlobalSetup]
    public void Setup() =>
        this.listener =
            this.sink.ListenStrong(static _ =>
            {
            });

    /// <summary>Removes the listener.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.listener?.Unlisten();

    /// <summary>Opens a transaction that does nothing, and closes it.</summary>
    [Benchmark(Baseline = true, Description = "empty transaction")]
    public void EmptyTransaction() =>
        Transaction.RunVoid(static () =>
        {
        });

    /// <summary>Sends one value to a sink that has one listener, with no transaction open.</summary>
    [Benchmark(Description = "send to a sink with one listener")]
    public void Send() => this.sink.Send(1);
}
