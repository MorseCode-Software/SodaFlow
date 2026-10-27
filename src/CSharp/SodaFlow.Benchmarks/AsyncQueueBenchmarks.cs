using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Async;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to cancel each item in a long queue of a MapAsync pipeline.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline uses the Queue strategy, and its operation waits until a
///         cancellation. Thus, one item runs. Each other item waits in the
///         queue. Both benchmarks make the pipeline and send the values. The
///         second one then cancels each item in one transaction, which ends each
///         item of the queue together. The first one does not dispose of the
///         pipeline, because a disposal also cancels each item. No code other than
///         the benchmark holds the pipeline, thus the garbage collector removes it.
///     </para>
///     <para>
///         An end finds its item in the queue by its id, and the removal of the
///         ends reads the queue again. A search for each id costs the product of
///         the number of ends and the length of the queue. Thus, for a full queue
///         the cost of a cancellation was quadratic.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class AsyncQueueBenchmarks
{
    /// <summary>How many values the pipeline gets.</summary>
    [Params(10, 100, 1_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>Makes the pipeline and sends the values.</summary>
    [Benchmark(Baseline = true, Description = "fill the queue")]
    public void Fill() => this.Run(cancel: false);

    /// <summary>The same, and cancels each item in one transaction.</summary>
    [Benchmark(Description = "fill the queue and cancel each item")]
    public void FillAndCancel() => this.Run(cancel: true);

    private void Run(bool cancel)
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();

        AsyncMapStatus<int, int> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static async (value, factory, token) =>
                {
                    await Task.Delay(millisecondsDelay: Timeout.Infinite, cancellationToken: token);
                    return factory.FromValue(value);
                },
                strategy: AsyncConcurrencyStrategy.Queue(),
                cancelAll: cancelAll);

        for (int i = 0; i < this.ItemCount; i++)
        {
            source.Send(i);
        }

        if (cancel)
        {
            cancelAll.Send(Unit.Value);
        }

        GC.KeepAlive(status);
    }
}
