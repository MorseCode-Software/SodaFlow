using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a post, a defer, and a split cost when no transaction fails.
/// </summary>
/// <remarks>
///     <para>
///         A posted action and a split item each run in a child transaction of the
///         transaction that deferred them. A child that fails drops the work that
///         it deferred. These benchmarks measure the other path, where each child
///         commits and its work goes to the queues of the first transaction.
///     </para>
///     <para>
///         The first benchmark defers from the first transaction only, thus it is
///         the cost that a child adds to. Each other benchmark defers from a child.
///         One posts from a posted action, one defers after a defer, and one defers
///         from each item of a split.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class DeferredWorkBenchmarks
{
    private const int SplitLength = 8;

    private static readonly IReadOnlyList<int> Items = [.. Enumerable.Range(start: 0, count: SplitLength)];

    private readonly StreamSink<int> deferSource = Stream.CreateSink<int>();
    private readonly StreamSink<int> deferTwiceSource = Stream.CreateSink<int>();
    private readonly StreamSink<IReadOnlyList<int>> splitSource = Stream.CreateSink<IReadOnlyList<int>>();

    private IStrongListener? deferListener;
    private IStrongListener? deferTwiceListener;
    private IStrongListener? splitListener;

    /// <summary>Builds each chain, with one listener at its end.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.deferListener =
            Operational.Defer(this.deferSource)
                .ListenStrong(static _ =>
                {
                });

        this.deferTwiceListener =
            Operational.Defer(Operational.Defer(this.deferTwiceSource))
                .ListenStrong(static _ =>
                {
                });

        this.splitListener =
            Operational.Defer(Operational.Split<int, IReadOnlyList<int>>(this.splitSource))
                .ListenStrong(static _ =>
                {
                });
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.deferListener?.Unlisten();
        this.deferTwiceListener?.Unlisten();
        this.splitListener?.Unlisten();
    }

    /// <summary>Posts one action from a transaction.</summary>
    [Benchmark(Baseline = true, Description = "post from a transaction")]
    public void Post() =>
        Transaction.RunVoid(static () =>
            Transaction.Post(static () =>
            {
            }));

    /// <summary>Posts one action from a transaction, and that action posts one more.</summary>
    [Benchmark(Description = "post from a posted action")]
    public void PostFromPostedAction() =>
        Transaction.RunVoid(static () =>
            Transaction.Post(static () =>
                Transaction.Post(static () =>
                {
                })));

    /// <summary>Sends one value through one defer.</summary>
    [Benchmark(Description = "defer")]
    public void Defer() => this.deferSource.Send(1);

    /// <summary>Sends one value through two defers. The second defer queues from a split item.</summary>
    [Benchmark(Description = "defer after a defer")]
    public void DeferTwice() => this.deferTwiceSource.Send(1);

    /// <summary>Splits a list, and defers each item. Each split item queues one deferred value.</summary>
    [Benchmark(Description = "defer from each of 8 split items")]
    public void DeferFromEachSplitItem() => this.splitSource.Send(Items);
}
