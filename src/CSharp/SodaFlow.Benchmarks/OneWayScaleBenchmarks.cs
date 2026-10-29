using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks;

/// <summary>
///     The cost of a one-way bindable value at the scale of a screen that binds
///     many of them. Each value has its own cell. One benchmark updates all cells in
///     one transaction and delivers each notification. That is the most work that
///     one transaction can give the binding thread. The other updates one cell,
///     which is the usual transaction.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class OneWayScaleBenchmarks
{
    private readonly ManualScheduler scheduler = new();
    private CellSink<int>[] cells = [];
    private int next;
    private IOneWayBindableValue<int>[] values = [];

    // ReSharper disable once UnassignedField.Global - BenchmarkDotNet sets it.
    [Params(100, 10_000)]
    public int Bindings;

    [GlobalSetup]
    public void Setup()
    {
        BindableFactory factory = new(this.scheduler);
        this.cells = new CellSink<int>[this.Bindings];
        this.values = new IOneWayBindableValue<int>[this.Bindings];

        for (int i = 0; i < this.Bindings; i++)
        {
            this.cells[i] = Cell.CreateSink(0);
            this.values[i] = factory.CreateOneWay(this.cells[i]);
            _ = this.values[i].Value;
        }
    }

    [Benchmark(Description = "send to every cell in one transaction, deliver all")]
    public void SendToAllAndDeliver()
    {
        int value = ++this.next;

        Transaction.RunVoid(() =>
        {
            // ReSharper disable once ForCanBeConvertedToForeach - The array is a field.
            for (int i = 0; i < this.cells.Length; i++)
            {
                this.cells[i].Send(value);
            }
        });

        this.scheduler.Drain();
    }

    [Benchmark(Description = "send to one cell, deliver one")]
    public void SendToOneAndDeliver()
    {
        this.cells[0].Send(++this.next);
        this.scheduler.Drain();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (IOneWayBindableValue<int> value in this.values)
        {
            value.Dispose();
        }
    }

    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ManualScheduler : IBindingScheduler
    {
        private readonly Queue<Action> queue = new();

        public bool CheckAccess() => true;

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
