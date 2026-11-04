using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to send into an operation that captures the value of each
///     input and puts the inputs together: a lift of two cells, a lift of six, a
///     snapshot of the latest values, and a merge of two types.
/// </summary>
/// <remarks>
///     Each operation keeps the value of an input that updated in this transaction
///     in a slot, and the step that puts the inputs together reads the slots. The
///     slot records the transaction that filled it, thus a transaction that fails
///     between the two leaves no value for the next one. These benchmarks measure
///     the cost of that record on the usual path, where no transaction fails. Each
///     one sends to one input, with a listener on the output.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class LiftBenchmarks
{
    private readonly CellSink<int> a = Cell.CreateSink(0);
    private readonly CellSink<int> b = Cell.CreateSink(0);
    private readonly CellSink<int> c = Cell.CreateSink(0);
    private readonly CellSink<int> d = Cell.CreateSink(0);
    private readonly CellSink<int> e = Cell.CreateSink(0);
    private readonly CellSink<int> f = Cell.CreateSink(0);
    private readonly CellSink<int> sixA = Cell.CreateSink(0);
    private readonly CellSink<int> listA = Cell.CreateSink(0);
    private readonly CellSink<int> bothA = Cell.CreateSink(0);
    private readonly CellSink<int> bothB = Cell.CreateSink(0);
    private readonly CellSink<int> applyArgument = Cell.CreateSink(0);
    private readonly CellSink<Func<int, int>> applyFunction = Cell.CreateSink<Func<int, int>>(static x => x);
    private readonly StreamSink<int> mergeFunctionLeft = Stream.CreateSink<int>();
    private readonly StreamSink<int> mergeFunctionRight = Stream.CreateSink<int>();
    private readonly StreamSink<int> snapshotSource = Stream.CreateSink<int>();
    private readonly CellSink<int> snapshotCell = Cell.CreateSink(0);
    private readonly StreamSink<int> mergeLeft = Stream.CreateSink<int>();
    private readonly StreamSink<string> mergeRight = Stream.CreateSink<string>();
    private readonly CellSink<int> switchFirst = Cell.CreateSink(0);
    private readonly CellSink<int> switchSecond = Cell.CreateSink(0);
    private readonly CellSink<Cell<int>> switchOuter;

    private IStrongListener? twoListener;
    private IStrongListener? sixListener;
    private IStrongListener? listListener;
    private IStrongListener? bothListener;
    private IStrongListener? applyListener;
    private IStrongListener? mergeFunctionListener;
    private IStrongListener? snapshotListener;
    private IStrongListener? mergeListener;
    private IStrongListener? switchListener;

    private int value;

    public LiftBenchmarks() => this.switchOuter = Cell.CreateSink<Cell<int>>(this.switchFirst);

    /// <summary>Builds the operations, each with one listener.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.twoListener =
            this.a.Lift(c2: this.b, f: static (x, y) => x + y)
                .Updates()
                .ListenStrong(static _ =>
                {
                });

        this.sixListener =
            this.sixA.Lift(
                    c2: this.b,
                    c3: this.c,
                    c4: this.d,
                    c5: this.e,
                    c6: this.f,
                    f: static (x, y, z, u, v, w) => x + y + z + u + v + w)
                .Updates()
                .ListenStrong(static _ =>
                {
                });

        this.listListener =
            new List<Cell<int>> { this.listA, this.b, this.c, this.d, this.e, this.f }
                .Lift()
                .Updates()
                .ListenStrong(static _ =>
                {
                });

        this.bothListener =
            this.bothA.Lift(c2: this.bothB, f: static (x, y) => x + y)
                .Updates()
                .ListenStrong(static _ =>
                {
                });

        this.applyListener =
            this.applyArgument.Apply(this.applyFunction)
                .Updates()
                .ListenStrong(static _ =>
                {
                });

        this.mergeFunctionListener =
            this.mergeFunctionLeft.Merge(s2: this.mergeFunctionRight, f: static (x, y) => x + y)
                .ListenStrong(static _ =>
                {
                });

        this.snapshotListener =
            this.snapshotSource.SnapshotLatest(c: this.snapshotCell, f: static (x, y) => x + y)
                .ListenStrong(static _ =>
                {
                });

        this.mergeListener =
            this.mergeLeft.Merge(this.mergeRight)
                .ListenStrong(static _ =>
                {
                });

        this.switchListener =
            this.switchOuter.SwitchC()
                .Updates()
                .ListenStrong(static _ =>
                {
                });
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.twoListener?.Unlisten();
        this.sixListener?.Unlisten();
        this.listListener?.Unlisten();
        this.bothListener?.Unlisten();
        this.applyListener?.Unlisten();
        this.mergeFunctionListener?.Unlisten();
        this.snapshotListener?.Unlisten();
        this.mergeListener?.Unlisten();
        this.switchListener?.Unlisten();
    }

    /// <summary>Sends a new value to one input of the lift of two cells.</summary>
    [Benchmark(Baseline = true, Description = "send to one input of a lift of two")]
    public void LiftOfTwo() => this.a.Send(++this.value);

    /// <summary>Sends a new value to one input of the lift of six cells.</summary>
    [Benchmark(Description = "send to one input of a lift of six")]
    public void LiftOfSix() => this.sixA.Send(++this.value);

    /// <summary>Sends a new value to the stream of the snapshot.</summary>
    [Benchmark(Description = "send to both inputs of a lift of two in one transaction")]
    public void LiftOfTwoBothInputs() =>
        Transaction.RunVoid(() =>
        {
            this.bothA.Send(++this.value);
            this.bothB.Send(this.value);
        });

    [Benchmark(Description = "send to one input of a lift of a list of six")]
    public void LiftOfListOfSix() => this.listA.Send(++this.value);

    [Benchmark(Description = "send to the argument of an apply")]
    public void ApplyArgument() => this.applyArgument.Send(++this.value);

    [Benchmark(Description = "send to the function of an apply")]
    public void ApplyFunction() => this.applyFunction.Send(static x => x + 1);

    [Benchmark(Description = "send to one stream of a merge of two with a function")]
    public void MergeWithFunction() => this.mergeFunctionLeft.Send(++this.value);

    [Benchmark(Description = "send to the stream of a snapshot of the latest values")]
    public void SnapshotLatest() => this.snapshotSource.Send(++this.value);

    /// <summary>Sends a new value to the cell of the snapshot, which fires nothing.</summary>
    [Benchmark(Description = "send to the cell of a snapshot of the latest values")]
    public void SnapshotLatestCell() => this.snapshotCell.Send(++this.value);

    /// <summary>Sends a new value to the left stream of the merge of two types.</summary>
    [Benchmark(Description = "send to one stream of a merge of two types")]
    public void MergeOfTwoTypes() => this.mergeLeft.Send(++this.value);

    /// <summary>Sends the other inner cell to the outer cell of the switch. Thus, each send switches.</summary>
    [Benchmark(Description = "send the other cell to the outer cell of a switch")]
    public void SwitchOuter() =>
        this.switchOuter.Send((++this.value & 1) == 0 ? this.switchFirst : this.switchSecond);
}
