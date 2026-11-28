using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Async;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs for one MapAsync item to complete later, on a different
///     thread, and give its result back to the graph.
/// </summary>
/// <remarks>
///     <para>
///         Each benchmark sends one value to a pipeline that this object made, and
///         waits until the result arrives at the listener of the results. The
///         operation yields before it gives its result. Thus, it completes on a
///         thread of the thread pool, and that thread opens the transaction that
///         ends the item and publishes the result. The cost is the admission, the
///         start, the move to the other thread, the end, the publish, and the
///         signal back to the thread of the benchmark.
///     </para>
///     <para>
///         <see cref="AsyncAdmissionBenchmarks" /> measures the same pipeline with
///         an operation that completed before the send. The difference between the
///         two is the path of a result that comes back later.
///     </para>
///     <para>
///         The Parallel strategy starts each item at its admission. The Queue
///         strategy starts an item only when no other item runs. Each send waits
///         for the result of the one before it, thus the queue is empty at each
///         send.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class AsyncCompletionBenchmarks
{
    private readonly Pipeline parallel = new(AsyncConcurrencyStrategy.Parallel());
    private readonly Pipeline queue = new(AsyncConcurrencyStrategy.Queue());

    /// <summary>Disposes of the two pipelines.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.parallel.Dispose();
        this.queue.Dispose();
    }

    /// <summary>Sends one value with the Parallel strategy, and waits for its result.</summary>
    [Benchmark(Baseline = true, Description = "a result that comes back later, Parallel strategy")]
    public void Parallel() => this.parallel.SendAndWait();

    /// <summary>Sends one value with the Queue strategy, and waits for its result.</summary>
    [Benchmark(Description = "a result that comes back later, Queue strategy")]
    public void Queue() => this.queue.SendAndWait();

    private sealed class Pipeline : IDisposable
    {
        private readonly ManualResetEventSlim arrived = new();
        private readonly IListener resultsListener;
        private readonly StreamSink<int> source = Stream.CreateSink<int>();
        private readonly AsyncMapStatus<int, int> status;

        public Pipeline(AsyncConcurrencyStrategyBase<Unit> strategy)
        {
            StreamSink<int> results = Stream.CreateSink<int>();
            StreamSink<Exception> errors = Stream.CreateSink<Exception>();

            this.status =
                this.source.MapAsync(
                    results: results,
                    errors: errors,
                    handleException: static e =>
                        throw new InvalidOperationException(
                            message: "The pipeline gave an exception to handleException.",
                            innerException: e),
                    operation: static async (value, factory, _) =>
                    {
                        // No synchronization context is on the thread of the benchmark, thus the
                        // rest of the operation runs on a thread of the thread pool.
                        await Task.Yield();

                        return factory.FromValue(value);
                    },
                    strategy: strategy);

            // The listener signals the thread of the benchmark when the result arrives.
            this.resultsListener = results.Listen(_ => this.arrived.Set());
        }

        public void SendAndWait()
        {
            this.arrived.Reset();
            this.source.Send(1);
            this.arrived.Wait();
        }

        public void Dispose()
        {
            this.resultsListener.Dispose();
            this.status.Dispose();
            this.arrived.Dispose();
        }
    }
}
