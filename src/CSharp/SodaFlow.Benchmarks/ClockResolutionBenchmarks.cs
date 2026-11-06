using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Time;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a clock with a resolution saves at each transaction, against the
///     clock with its value as it is.
/// </summary>
/// <remarks>
///     <para>
///         Each arm runs an empty transaction with one timer system alive and one
///         listener on the updates of <c>Time</c>. That listener is what a
///         resolution changes: a clock value in the step of the last one sends
///         nothing, and the listener does not run. The transactions of an arm are
///         less than one millisecond apart. Thus, with a resolution of one
///         millisecond, most of them skip the send, and the arm shows the skip.
///         With the value as it is, each transaction sends.
///     </para>
///     <para>
///         <see cref="ClockReadBenchmarks" /> has the read of the clock and the
///         rounding on their own.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class ClockResolutionBenchmarks
{
    private IDisposable? timers;
    private IListener? listener;

    [Params(ClockKind.Stopwatch, ClockKind.UtcNow)]
    public ClockKind Clock { get; [UsedImplicitly] set; }

    [Params(0, 1)]
    public int ResolutionMilliseconds { get; [UsedImplicitly] set; }

    [GlobalSetup]
    public void Setup()
    {
        TimeSpan resolution = TimeSpan.FromMilliseconds(this.ResolutionMilliseconds);

        if (this.Clock == ClockKind.Stopwatch)
        {
            SecondsTimerSystem seconds = new(resolution: resolution, handleException: Ignore);
            this.timers = seconds;
            this.listener = Operational.Updates(seconds.Time).Listen(Nothing);
        }
        else
        {
            UtcClockTimerSystem utc = new(resolution: resolution, handleException: Ignore);
            this.timers = utc;
            this.listener = Operational.Updates(utc.Time).Listen(Nothing);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.listener?.Unlisten();
        this.timers?.Dispose();
    }

    [Benchmark(Description = "empty transaction with the clock alive")]
    public void EmptyTransaction() =>
        Transaction.RunVoid(static () =>
        {
        });

    private static void Ignore(Exception e)
    {
    }

    private static void Nothing<T>(T value)
    {
    }

    // ReSharper disable once MemberCanBeInternal - BenchmarkDotNet reads a parameter of a public property, and the
    // property's type must be as visible as the property.
    public enum ClockKind
    {
        Stopwatch,
        UtcNow
    }
}
