using System;
using System.Collections.Generic;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to send one update from the graph to a bound property. It
///     also gives how much of that is the two-way value sampling its cell.
/// </summary>
/// <remarks>
///     <para>
///         The two-way value wrote back the value that the update carried. It now
///         samples the cell, thus a late update cannot put a stale value on the
///         screen. A sample out of a transaction opens one, and a transaction
///         takes a process-wide lock. Thus, the change has a cost, and the
///         question of how much it costs is a fair one.
///     </para>
///     <para>
///         The scheduler here queues, and other code drains it explicitly, which
///         is what a dispatcher does and is the condition that matters. The work
///         goes in the queue in a transaction and runs at its end, with no
///         transaction in flight. Thus, the sample has to open its own. With
///         <see cref="ImmediateBindingScheduler" /> the refresh runs while the
///         sending transaction is open and joins it, which has a lower cost and
///         flatters the numbers.
///     </para>
///     <para>
///         One-way values are the contrast and not a control. They were not
///         changed, and they write back the value the update carried, thus the
///         difference between the two is what a sample costs. Most bindings are
///         one-way, and pay none of this.
///     </para>
///     <para>
///         The arm for a command changes its availability. The arm on a context
///         uses <see cref="SynchronizationContextBindingScheduler" />, which holds
///         the posts of a transaction and sends them as one item.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class BindableRefreshBenchmarks
{
    private readonly IBindableAction<int> command;

    private readonly CellSink<bool> commandEnabledCell = Cell.CreateSink(false);

    private readonly ManualScheduler commandScheduler = new();

    private readonly ManualContext context = new();

    private readonly IOneWayBindableValue<int> oneWay;

    private readonly CellSink<int> oneWayCell = Cell.CreateSink(0);

    private readonly IOneWayBindableValue<int> oneWayOnAContext;

    private readonly CellSink<int> oneWayOnAContextCell = Cell.CreateSink(0);

    private readonly ManualScheduler oneWayScheduler = new();

    private readonly ITwoWayBindableValue<int> twoWay;

    private readonly CellSink<int> twoWayCell = Cell.CreateSink(0);

    private readonly ManualScheduler twoWayScheduler = new();

    private readonly CellSink<int> unboundCell = Cell.CreateSink(0);

    private int next;

    private bool nextEnabled;

    /// <summary>
    ///     Builds the bound cells. A constructor rather than a global setup, so
    ///     the fields can stay readonly and non-nullable.
    /// </summary>
    public BindableRefreshBenchmarks()
    {
        this.oneWay = new BindableFactory(this.oneWayScheduler).CreateOneWay(this.oneWayCell);
        this.twoWay = new BindableFactory(this.twoWayScheduler).CreateTwoWay(this.twoWayCell);

        this.command =
            new BindableFactory(this.commandScheduler).CreateBindableAction(
                firingsStreamSink: Stream.CreateSink<int>(),
                isEnabledCell: this.commandEnabledCell);

        this.oneWayOnAContext =
            new BindableFactory(new SynchronizationContextBindingScheduler(this.context))
                .CreateOneWay(this.oneWayOnAContextCell);
    }

    /// <summary>
    ///     One update into a cell nothing is bound to: the transaction and the graph,
    ///     with no bindable in the path. The baseline, so the ratios read as what
    ///     binding adds.
    /// </summary>
    [Benchmark(Baseline = true, Description = "send, nothing bound")]
    public void SendToAnUnboundCell() => this.unboundCell.Send(++this.next);

    /// <summary>One update to a one-way value, which writes back the value it got.</summary>
    [Benchmark(Description = "send and deliver, one-way")]
    public void SendAndDeliverOneWay()
    {
        this.oneWayCell.Send(++this.next);
        this.oneWayScheduler.Drain();
    }

    /// <summary>
    ///     One update to a two-way value, which samples the cell. The sample opens a
    ///     transaction of its own, since the drain runs with none in flight.
    /// </summary>
    [Benchmark(Description = "send and deliver, two-way")]
    public void SendAndDeliverTwoWay()
    {
        this.twoWayCell.Send(++this.next);
        this.twoWayScheduler.Drain();
    }

    /// <summary>
    ///     One change of the availability of a command. Each change is a different
    ///     value, thus the command writes its copy each time.
    /// </summary>
    [Benchmark(Description = "change and deliver, command enablement")]
    public void ChangeAndDeliverCommandEnablement()
    {
        this.nextEnabled = !this.nextEnabled;
        this.commandEnabledCell.Send(this.nextEnabled);
        this.commandScheduler.Drain();
    }

    /// <summary>
    ///     One update to a one-way value through the scheduler on a context. The
    ///     scheduler adds a batch for the transaction to the cost of the arm above.
    /// </summary>
    [Benchmark(Description = "send and deliver, one-way, context scheduler")]
    public void SendAndDeliverOneWayOnAContext()
    {
        this.oneWayOnAContextCell.Send(++this.next);
        this.context.Drain();
    }

    /// <summary>The added operation on its own: a sample with no open transaction.</summary>
    [Benchmark(Description = "sample, no transaction open")]
    public int SampleOutsideATransaction() => this.twoWayCell.Sample();

    /// <summary>
    ///     An empty transaction, which is the floor below the sample above. It gives
    ///     the cost to get the lock, to open, and to close, with nothing in between.
    /// </summary>
    [Benchmark(Description = "empty transaction")]
    public int EmptyTransaction() => Transaction.Run(static () => 0);

    /// <summary>Keeps the bindable objects in memory, and their listeners with them.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.oneWay.Dispose();
        this.twoWay.Dispose();
        this.command.Dispose();
        this.oneWayOnAContext.Dispose();
    }

    /// <summary>Queues like a dispatcher, and runs only when asked.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ManualScheduler : IBindingScheduler
    {
        private readonly Queue<Action> queue = new();

        /// <inheritdoc />
        public bool CheckAccess() => true;

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

    /// <summary>A context that queues like a dispatcher, and runs only when asked.</summary>
    // ReSharper disable once InheritdocConsiderUsage - The summary says what this context is for, which the base
    // cannot.
    private sealed class ManualContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object? state) => this.queue.Enqueue((d, state));

        internal void Drain()
        {
            while (this.queue.Count > 0)
            {
                (SendOrPostCallback callback, object? state) = this.queue.Dequeue();
                callback(state);
            }
        }
    }
}
