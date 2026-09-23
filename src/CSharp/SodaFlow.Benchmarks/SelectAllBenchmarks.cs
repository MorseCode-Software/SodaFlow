using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     The select-all graph. It is a list of objects that each hold a selection cell. Above that
///     is a cell over the full list, from a lift of theirs, and a tri-state "all selected" that
///     goes back through a loop. Thus, one toggle drives each element.
/// </summary>
/// <remarks>
///     <para>
///         This is the shape the library exists for, and the one that stresses it. It has a loop, a
///         lift over each element, and a switch that rebuilds the lift when the collection changes.
///         It came from SodaFlow.Tests.Performance, a console harness deleted after this replaced it.
///         There, a stopwatch timed the graph around a sequence with twenty-five half-second sleeps.
///         Thus, the number it printed was mostly sleep, and a read of it meant a run with key
///         presses. This measures one
///         operation at a time, which is the thing worth a measurement, and the thing to compare
///         between runs.
///     </para>
///     <para>
///         Element count is a parameter because how these scale is the question. A toggle touches
///         each element. A replacement of the collection rebuilds the lift.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class SelectAllBenchmarks
{
    // Populated fully in the setup. built small here so the field never has to be nullable.
    private Graph graph = Graph.Build(1);

    private bool nextSelection;

    /// <summary>How many selectable objects the graph holds.</summary>
    [Params(100, 1000)]
    public int ObjectCount { get; [UsedImplicitly] set; }

    /// <summary>Builds a graph of <see cref="ObjectCount" /> objects for the benchmarks to work on.</summary>
    [GlobalSetup]
    public void Setup() => this.graph = Graph.Build(this.ObjectCount);

    /// <summary>Releases the listener holding the graph up.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.graph.Listener.Unlisten();

    /// <summary>Builds the graph and fills it, which is what a view model does one time.</summary>
    [Benchmark(Description = "build the graph")]
    public int BuildTheGraph()
    {
        Graph built = Graph.Build(this.ObjectCount);
        built.Listener.Unlisten();

        return built.Objects.Sample().Count;
    }

    /// <summary>One toggle, which flips each element and recomputes the tri-state above them.</summary>
    [Benchmark(Description = "toggle all selected")]
    public void ToggleAllSelected() => this.graph.ToggleAllSelected.Send(Unit.Value);

    /// <summary>One element changing, which recomputes the lift but touches one source.</summary>
    [Benchmark(Description = "select one object")]
    public void SelectOneObject()
    {
        this.nextSelection = !this.nextSelection;
        this.graph.Objects.Sample()[0].IsSelectedStreamSink.Send(this.nextSelection);
    }

    /// <summary>Replacing the collection, which rebuilds the lift the switch feeds.</summary>
    [Benchmark(Description = "replace every object")]
    public void ReplaceEveryObject() => this.graph.Objects.Send(this.graph.NewObjects(this.ObjectCount));

    /// <summary>One selectable thing: its own toggle, and the select-all stream folded in.</summary>
    private sealed class TestObject
    {
        internal TestObject(Stream<bool> selectAllStream)
        {
            this.IsSelectedStreamSink = Stream.CreateSink<bool>();
            this.IsSelected = selectAllStream.OrElse(this.IsSelectedStreamSink).Hold(false);
        }

        internal Cell<bool> IsSelected { get; }

        internal StreamSink<bool> IsSelectedStreamSink { get; }
    }

    /// <summary>
    ///     A built graph, so the fields holding one can stay non-nullable across a parameterized
    ///     setup.
    /// </summary>
    private sealed class Graph
    {
        private Graph(
            StreamSink<Unit> toggleAllSelected,
            CellSink<IReadOnlyList<TestObject>> objects,
            Stream<bool> selectAllStream,
            IListener listener)
        {
            this.ToggleAllSelected = toggleAllSelected;
            this.Objects = objects;
            this.SelectAllStream = selectAllStream;
            this.Listener = listener;
        }

        internal IListener Listener { get; }

        internal CellSink<IReadOnlyList<TestObject>> Objects { get; }

        private Stream<bool> SelectAllStream { get; }

        internal StreamSink<Unit> ToggleAllSelected { get; }

        /// <summary>Builds the full graph and populates it with <paramref name="count" /> objects.</summary>
        internal static Graph Build(int count)
        {
            (StreamSink<Unit> toggleAllSelected,
                    Cell<IReadOnlyList<(TestObject Object, bool IsSelected)>> objectsAndIsSelected,
                    Stream<bool> selectAllStream,
                    CellSink<IReadOnlyList<TestObject>> objects) =
                Transaction.Run(static () =>
                    Cell.Loop<bool?>()
                        .WithCaptures(static allSelectedLoop =>
                        {
                            StreamSink<Unit> toggle = Stream.CreateSink<Unit>();

                            Stream<bool> selectAll = toggle.Snapshot(allSelectedLoop).Map(static a => a != true);

                            CellSink<IReadOnlyList<TestObject>> objectsSink =
                                Cell.CreateSink((IReadOnlyList<TestObject>)[]);

                            Cell<IReadOnlyList<(TestObject Object, bool IsSelected)>> lifted =
                                objectsSink
                                    .Map(static oo =>
                                        oo.Select(static o => o.IsSelected.Map(s => (Object: o, IsSelected: s))).Lift())
                                    .SwitchC();

                            Cell<bool?> allSelected =
                                lifted.Map(static oo =>
                                    oo.Count == 0
                                        ? true
                                        : oo.All(static o => o.IsSelected)
                                            ? true
                                            : oo.All(static o => !o.IsSelected)
                                                ? (bool?)false
                                                : null);

                            return (Cell: allSelected, Captures: (toggle, lifted, selectAll, objectsSink));
                        })
                        .Captures);

            // Something has to be listening, or the lift and the switch above are never evaluated
            // and the benchmark measures a graph nobody asked anything of. Counting is the cheapest
            // question to answer.
            IListener listener =
                Transaction.Run(() =>
                    objectsAndIsSelected
                        .Map(static oo => oo.Count(static o => o.IsSelected))
                        .Updates()
                        .ListenStrong(static _ =>
                        {
                        }));

            Graph graph =
                new(
                    toggleAllSelected: toggleAllSelected,
                    objects: objects,
                    selectAllStream: selectAllStream,
                    listener: listener);

            graph.Objects.Send(graph.NewObjects(count));

            return graph;
        }

        /// <summary>A new set of objects wired to the select-all stream of this graph.</summary>
        internal IReadOnlyList<TestObject> NewObjects(int count) =>
        [
            .. Enumerable.Range(start: 0, count: count).Select(_ => new TestObject(this.SelectAllStream))
        ];
    }
}
