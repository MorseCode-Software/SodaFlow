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
    private readonly StreamSink<int> snapshotSource = Stream.CreateSink<int>();
    private readonly CellSink<int> snapshotCell = Cell.CreateSink(0);
    private readonly StreamSink<int> mergeLeft = Stream.CreateSink<int>();
    private readonly StreamSink<string> mergeRight = Stream.CreateSink<string>();

    private IStrongListener? twoListener;
    private IStrongListener? sixListener;
    private IStrongListener? snapshotListener;
    private IStrongListener? mergeListener;

    private int value;

    /// <summary>Builds the four operations, each with one listener.</summary>
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
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.twoListener?.Unlisten();
        this.sixListener?.Unlisten();
        this.snapshotListener?.Unlisten();
        this.mergeListener?.Unlisten();
    }

    /// <summary>Sends a new value to one input of the lift of two cells.</summary>
    [Benchmark(Baseline = true, Description = "send to one input of a lift of two")]
    public void LiftOfTwo() => this.a.Send(++this.value);

    /// <summary>Sends a new value to one input of the lift of six cells.</summary>
    [Benchmark(Description = "send to one input of a lift of six")]
    public void LiftOfSix() => this.sixA.Send(++this.value);

    /// <summary>Sends a new value to the stream of the snapshot.</summary>
    [Benchmark(Description = "send to the stream of a snapshot of the latest values")]
    public void SnapshotLatest() => this.snapshotSource.Send(++this.value);

    /// <summary>Sends a new value to the cell of the snapshot, which fires nothing.</summary>
    [Benchmark(Description = "send to the cell of a snapshot of the latest values")]
    public void SnapshotLatestCell() => this.snapshotCell.Send(++this.value);

    /// <summary>Sends a new value to the left stream of the merge of two types.</summary>
    [Benchmark(Description = "send to one stream of a merge of two types")]
    public void MergeOfTwoTypes() => this.mergeLeft.Send(++this.value);
}
