using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to deliver one update from the graph to a bound property, and how much of
///     that is the two-way value sampling its cell.
/// </summary>
/// <remarks>
///     <para>
///         The two-way value used to write back whatever the update carried. It samples the cell
///         instead, so that an update delivered late cannot put a stale value on screen. Sampling
///         outside a transaction opens one, and a transaction takes a process-wide lock, so the
///         change is not free and the question of how much it costs is a fair one.
///     </para>
///     <para>
///         The scheduler here queues and is drained explicitly, which is what a dispatcher does
///         and is the case that matters: work posted from inside a transaction and run after it
///         closes, with no transaction in flight, so the sample has to open its own. Under
///         <see cref="ImmediateBindingScheduler" /> the refresh runs while the sending
///         transaction is still open and joins it instead, which is cheaper and would flatter
///         the numbers.
///     </para>
///     <para>
///         One-way values are the contrast rather than a control: they were not changed and still
///         write back the value the update carried, so the gap between the two is what sampling
///         costs. Most bindings in an application are one-way, and pay none of this.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net80)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class BindableRefreshBenchmarks
{
    private readonly IOneWayBindableValue<int> oneWay;

    private readonly CellSink<int> oneWayCell = Cell.CreateSink(0);

    private readonly ManualScheduler oneWayScheduler = new();

    private readonly ITwoWayBindableValue<int> twoWay;

    private readonly CellSink<int> twoWayCell = Cell.CreateSink(0);

    private readonly ManualScheduler twoWayScheduler = new();

    private readonly CellSink<int> unboundCell = Cell.CreateSink(0);

    private int next;

    /// <summary>
    ///     Builds the two bound cells. A constructor rather than a global setup, so the fields can
    ///     stay readonly and non-nullable.
    /// </summary>
    public BindableRefreshBenchmarks()
    {
        this.oneWay = this.oneWayCell.ToOneWay(scheduler: this.oneWayScheduler);
        this.twoWay = this.twoWayCell.ToTwoWay(scheduler: this.twoWayScheduler);
    }

    /// <summary>
    ///     One update into a cell nothing is bound to: the transaction and the graph, with no
    ///     bindable in the way. The baseline, so the ratios read as what binding adds.
    /// </summary>
    [Benchmark(Baseline = true, Description = "send, nothing bound")]
    public void SendToAnUnboundCell() => this.unboundCell.Send(++this.next);

    /// <summary>One update delivered to a one-way value, which writes back what it was handed.</summary>
    [Benchmark(Description = "send and deliver, one-way")]
    public void SendAndDeliverOneWay()
    {
        this.oneWayCell.Send(++this.next);
        this.oneWayScheduler.Drain();
    }

    /// <summary>
    ///     One update delivered to a two-way value, which samples the cell — opening a transaction
    ///     of its own, since the drain runs with none in flight.
    /// </summary>
    [Benchmark(Description = "send and deliver, two-way")]
    public void SendAndDeliverTwoWay()
    {
        this.twoWayCell.Send(++this.next);
        this.twoWayScheduler.Drain();
    }

    /// <summary>The added operation on its own: a sample with no transaction to join.</summary>
    [Benchmark(Description = "sample, no transaction open")]
    public int SampleOutsideATransaction() => this.twoWayCell.Sample();

    /// <summary>
    ///     An empty transaction, which is the floor under the sample above: what it costs to take
    ///     the lock, open and close, having done nothing in between.
    /// </summary>
    [Benchmark(Description = "empty transaction")]
    public int EmptyTransaction() => Transaction.Run(static () => 0);

    /// <summary>Keeps the bindable objects from being collected, and their listeners with them.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.oneWay.Dispose();
        this.twoWay.Dispose();
    }

    /// <summary>Queues like a dispatcher, and runs only when asked.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ManualScheduler : IBindingScheduler
    {
        private readonly Queue<Action> queue = new();

        /// <inheritdoc />
        public bool IsOnBindingThread => true;

        /// <inheritdoc />
        public void Post(Action action) => this.queue.Enqueue(action);

        internal void Drain()
        {
            while (this.queue.Count > 0)
            {
                this.queue.Dequeue()();
            }
        }
    }
}
