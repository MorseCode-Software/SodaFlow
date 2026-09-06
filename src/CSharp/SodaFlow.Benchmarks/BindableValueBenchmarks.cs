using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to read a bindable's <c>Value</c>, and how much of that is the check that the
///     read is happening on the binding thread.
/// </summary>
/// <remarks>
///     <para>
///         The check exists because the cached value behind <c>Value</c> is an ordinary field, safe
///         only while the property is touched on one thread; reading it from anywhere else used to
///         return a stale value silently. It is on the path a binding engine walks for every
///         refresh, so what it costs is worth knowing rather than assuming - the first
///         measurement of it came out an order of magnitude above the guess that preceded it.
///     </para>
///     <para>
///         The unguarded read is the baseline, so the ratio column reads directly as what the
///         check costs. The two <c>CheckAccess()</c> benchmarks isolate it further, and the
///         two <c>Value</c> benchmarks are what a binding engine pays per refresh - though not
///         what a binding costs, since neither includes the reflection nor compiled accessor the
///         engine reaches the property through.
///     </para>
///     <para>
///         Both runtimes, because the answer differs between them and the difference is the whole
///         reason the check is written the way it is. Reading
///         <see cref="System.Threading.SynchronizationContext.Current" /> on .NET Framework goes
///         through the execution context and costs real time; on modern .NET it is close to free.
///         The check compares the thread id first for that reason, and this is where that claim
///         can be checked rather than taken on trust.
///     </para>
///     <para>
///         There is deliberately no plain-property benchmark for scale. One was tried: the JIT
///         hoists the read out of the measurement loop whatever is done to the property, so it
///         measured zero and BenchmarkDotNet said so. A baseline the ratio column can use is more
///         useful than a floor it cannot.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net80)]
// Not sealed, and not private to this file, whatever the inspections say: BenchmarkDotNet
// generates a class deriving from this one and finds it by reflection, so neither "has no
// inheritors" nor "nothing here uses it" is true. Sealing it fails at run time rather than
// build time - "Declaring type must be unsealed" - which is a poor way to find out.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class BindableValueBenchmarks
{
    private readonly SynchronizationContextBindingScheduler affineScheduler =
        new(new SynchronizationContext());

    private readonly IBindingScheduler immediateScheduler = BindingScheduler.Immediate;

    private readonly IOneWayBindableValue<int> valueWithAffineScheduler =
        Cell.CreateSink(1)
            .ToOneWay(scheduler: new SynchronizationContextBindingScheduler(new SynchronizationContext()));

    private readonly IOneWayBindableValue<int> valueWithImmediateScheduler =
        Cell.CreateSink(1).ToOneWay(scheduler: BindingScheduler.Immediate);

    /// <summary>The check where the scheduler has no thread of its own, so it answers a constant.</summary>
    [Benchmark(Description = "CheckAccess(), no affinity")]
    public bool CheckWithoutAffinity() => this.immediateScheduler.CheckAccess();

    /// <summary>
    ///     The check where the scheduler has a thread. A thread-id comparison, and a context
    ///     comparison only if that one fails — the order matters, and this is what says by how
    ///     much.
    /// </summary>
    [Benchmark(Description = "CheckAccess(), affine")]
    public bool CheckWithAffinity() => this.affineScheduler.CheckAccess();

    /// <summary>
    ///     The floor for a guarded read: the check is a constant, so this is dispatch plus a field.
    ///     The baseline, so that the ratio column reads as what affinity costs.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Value, no affinity")]
    public int ReadValueWithoutAffinity() => this.valueWithImmediateScheduler.Value;

    /// <summary>What a binding engine pays per refresh against a real scheduler.</summary>
    [Benchmark(Description = "Value, affine")]
    public int ReadValueWithAffinity() => this.valueWithAffineScheduler.Value;
}
