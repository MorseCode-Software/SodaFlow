using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to increase the rank of each node in a chain.
/// </summary>
/// <remarks>
///     <para>
///         A link to a node gives that node a rank above its source. Then each
///         node that the node feeds must get a higher rank too. A loop that closes
///         onto a stream of higher rank does this for each node of the loop. Both
///         benchmarks make a chain of maps. The looped chain then closes its loop
///         onto a stream of higher rank, and the plain chain does not. Thus, the
///         difference between the two is the walk that increases the ranks.
///     </para>
///     <para>
///         The walk used a recursion, with one stack frame for each node in the
///         chain, and 10,000 nodes stopped the process with a stack overflow. Now
///         it keeps its own stack. These benchmarks stay below that length, thus
///         a run can compare the two forms of the walk.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file, at each report from the inspections.
// BenchmarkDotNet generates a class that derives from this one and finds it by reflection.
// Thus, "has no inheritors" is not true, and "nothing here uses it" is not true. A seal fails at run
// time and not at build time, with "Declaring type must be unsealed", which is a poor
// procedure to find this.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class RankPropagationBenchmarks
{
    private readonly StreamSink<int> sink = Stream.CreateSink<int>();

    /// <summary>
    ///     The number of maps in the chain.
    /// </summary>
    [Params(10, 100, 1000)]
    public int Length { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Makes the chain in a transaction, with no change of rank after it.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Chain")]
    public Stream<int> Chain() =>
        Transaction.Run(() =>
        {
            Stream<int> chain = this.sink;

            for (int i = 0; i < this.Length; i++)
            {
                chain = chain.Map(static v => v + 1);
            }

            return chain;
        });

    /// <summary>
    ///     Makes the chain in a loop, and closes the loop onto a stream of higher
    ///     rank. Thus, each node in the chain gets a new rank.
    /// </summary>
    [Benchmark(Description = "Looped chain, closed onto a higher rank")]
    public Stream<int> LoopedChain() =>
        Stream.Loop<int>()
            .WithCaptures(l =>
            {
                Stream<int> chain = l;

                for (int i = 0; i < this.Length; i++)
                {
                    chain = chain.Map(static v => v + 1);
                }

                return (Stream: this.sink.Map(static v => v), Captures: chain);
            })
            .Captures;
}
