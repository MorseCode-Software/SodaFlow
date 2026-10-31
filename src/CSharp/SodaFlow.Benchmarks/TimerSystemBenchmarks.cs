using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Time;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a timer system adds to each transaction in the process.
/// </summary>
/// <remarks>
///     A timer system installs a start hook, and that hook runs at the start of
///     each transaction in the process, not only of those that read the time.
///     Thus, the empty transaction of <see cref="TransactionBenchmarks" /> costs
///     more while a timer system is alive, and this class measures the difference.
///     The hook is process-wide. Thus, the timer system lives in the setup of a
///     class of its own, where it cannot tax the other benchmarks. The row with
///     <see cref="Disposed" /> true shows that a disposal removes the tax.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class TimerSystemBenchmarks
{
    private SecondsTimerSystem? timers;

    /// <summary>Gets or sets a value that is true where the setup disposes the timer system again.</summary>
    [Params(false, true)]
    public bool Disposed { get; [UsedImplicitly] set; }

    /// <summary>Makes the timer system, which installs its start hook, and disposes it where the row asks.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.timers =
            new SecondsTimerSystem(static _ =>
            {
            });

        if (this.Disposed)
        {
            this.timers.Dispose();
        }
    }

    /// <summary>Disposes the timer system.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.timers?.Dispose();

    /// <summary>Opens a transaction that does nothing, and closes it, with the timer system alive.</summary>
    [Benchmark(Description = "empty transaction with a timer system")]
    public void EmptyTransaction() =>
        Transaction.RunVoid(static () =>
        {
        });
}
