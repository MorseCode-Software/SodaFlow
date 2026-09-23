using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to read the <c>Value</c> of a bindable. It also gives how much of that is the
///     check that the read is on the binding thread.
/// </summary>
/// <remarks>
///     <para>
///         The check exists because the cached value behind <c>Value</c> is an ordinary field, safe
///         only while one thread touches the property. A read of it from anywhere else used to
///         give a stale value with no message. It is on the path a binding engine walks for each
///         refresh, thus its cost is worth a measurement. The first
///         measurement of it came out an order of magnitude above the guess that preceded it.
///     </para>
///     <para>
///         The unguarded read is the baseline, so the ratio column reads directly as what the
///         check costs. The two <c>CheckAccess()</c> benchmarks isolate it more. The
///         two <c>Value</c> benchmarks are what a binding engine pays for each refresh. That is not
///         what a binding costs. No one of the two holds the reflection or the compiled accessor that
///         the engine reaches the property through.
///     </para>
///     <para>
///         The two runtimes, because the answer differs between them and the difference is the full
///         cause of this shape for the check. A read of
///         <see cref="System.Threading.SynchronizationContext.Current" /> on .NET Framework goes
///         through the execution context and costs measurable time. On modern .NET it is close to free.
///         The check compares the thread id first because of that. This is where a reader can check
///         that claim, and does not have to trust it.
///     </para>
///     <para>
///         There is deliberately no plain-property benchmark for scale. A try of one showed that the JIT
///         moves the read out of the measurement loop, at each change to the property. Thus, it
///         measured zero and BenchmarkDotNet said so. A baseline the ratio column can use is more
///         useful than a floor it cannot.
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
    ///     The check where the scheduler has a thread. A thread-id compare, and a context
    ///     compare only if that one fails — the order matters, and this is what says by how
    ///     much.
    /// </summary>
    [Benchmark(Description = "CheckAccess(), affine")]
    public bool CheckWithAffinity() => this.affineScheduler.CheckAccess();

    /// <summary>
    ///     The floor for a guarded read: the check is a constant, thus this is one virtual call and one field.
    ///     The baseline, so that the ratio column reads as what affinity costs.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Value, no affinity")]
    public int ReadValueWithoutAffinity() => this.valueWithImmediateScheduler.Value;

    /// <summary>What a binding engine pays for each refresh against a true scheduler.</summary>
    [Benchmark(Description = "Value, affine")]
    public int ReadValueWithAffinity() => this.valueWithAffineScheduler.Value;
}
