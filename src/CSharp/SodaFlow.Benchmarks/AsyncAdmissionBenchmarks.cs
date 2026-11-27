using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Async;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to admit one value to a MapAsync pipeline.
/// </summary>
/// <remarks>
///     <para>
///         Each benchmark sends one value to a pipeline that this object made. The
///         operation gives back one Task that completed before the run. Thus, each
///         item starts and ends at its send, and no item waits. The cost is the
///         admission, the start, the end, and the publish of one item, and not the
///         operation.
///     </para>
///     <para>
///         The Parallel strategy starts each item at its admission. The Queue
///         strategy starts an item only when no other item runs. The queue is
///         empty at each send, thus it also starts each item at its admission,
///         through its own path.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class AsyncAdmissionBenchmarks
{
    // The one Task that each operation gives back. The first call makes it.
    private static Task<MapAsyncResult<int>>? completed;

    private readonly Pipeline parallel = new(AsyncConcurrencyStrategy.Parallel());
    private readonly Pipeline queue = new(AsyncConcurrencyStrategy.Queue());

    /// <summary>Disposes of the two pipelines.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.parallel.Dispose();
        this.queue.Dispose();
    }

    /// <summary>Admits one value with the Parallel strategy.</summary>
    [Benchmark(Baseline = true, Description = "admit one value, Parallel strategy")]
    public void Parallel() => this.parallel.Source.Send(1);

    /// <summary>Admits one value with the Queue strategy.</summary>
    [Benchmark(Description = "admit one value, Queue strategy")]
    public void Queue() => this.queue.Source.Send(1);

    private sealed class Pipeline : IDisposable
    {
        private readonly IListener resultsListener;
        private readonly AsyncMapStatus<int, int> status;

        public Pipeline(AsyncConcurrencyStrategyBase<Unit> strategy)
        {
            StreamSink<int> results = Stream.CreateSink<int>();
            StreamSink<Exception> errors = Stream.CreateSink<Exception>();

            this.status =
                this.Source.MapAsync(
                    results: results,
                    errors: errors,
                    handleException: static e =>
                        throw new InvalidOperationException(
                            message: "The pipeline gave an exception to handleException.",
                            innerException: e),
                    operation: static (_, factory, _) => completed ??= Task.FromResult(factory.FromValue(7)),
                    strategy: strategy);

            // A listener on the results, thus each publish has a target, as in an application.
            this.resultsListener = results.Listen(static _ => { });
        }

        public StreamSink<int> Source { get; } = Stream.CreateSink<int>();

        public void Dispose()
        {
            this.resultsListener.Dispose();
            this.status.Dispose();
        }
    }
}
