using System;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one read of each clock of the package costs, as it is and rounded
///     down to a millisecond.
/// </summary>
/// <remarks>
///     The arms copy the arithmetic of the clocks, because the clocks keep it
///     private. A <see cref="Stopwatch" /> rounds in its own ticks, and
///     <see cref="DateTime.UtcNow" /> rounds in the ticks of a
///     <see cref="DateTime" />. <see cref="ClockResolutionBenchmarks" /> has what
///     the rounding saves at a transaction.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class ClockReadBenchmarks
{
    private static readonly long StopwatchTicksPerMillisecond = Stopwatch.Frequency / 1000;

    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    [Benchmark(Description = "stopwatch, reading as it is")]
    public double StopwatchAsItIs() => this.stopwatch.Elapsed.TotalSeconds;

    [Benchmark(Description = "stopwatch, rounded to a millisecond")]
    public double StopwatchRounded()
    {
        long ticks = this.stopwatch.ElapsedTicks;
        return (ticks - ticks % StopwatchTicksPerMillisecond) / (double)Stopwatch.Frequency;
    }

    [Benchmark(Description = "UtcNow, reading as it is")]
    public DateTime UtcNowAsItIs() => DateTime.UtcNow;

    [Benchmark(Description = "UtcNow, rounded to a millisecond")]
    public DateTime UtcNowRounded()
    {
        DateTime time = DateTime.UtcNow;
        return new DateTime(ticks: time.Ticks - time.Ticks % TimeSpan.TicksPerMillisecond, kind: time.Kind);
    }
}
