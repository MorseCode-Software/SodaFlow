using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Time;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What the timer base pays to put a timer in order among the timers that
///     wait, and to take one out again.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="TimerSystemImplementationBase{T}" /> holds its timers in a
///         sorted set. Each add, each cancel, and each fire compares a timer
///         against <c>O(log n)</c> of the timers in the set. Each of those
///         comparisons goes through the time type's comparison. The two arms are
///         the two paths that a timer takes. The first is an add that a cancel
///         follows, which is the usual life of a timeout that does not fire. The
///         second is an add that a fire follows.
///     </para>
///     <para>
///         The clock here does not start its thread. The arms call the base
///         directly, thus they measure the set and the comparison, and not a wait or
///         a transaction.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class TimerSchedulingBenchmarks
{
    private static readonly Action Nothing = static () =>
    {
    };

    // The timers that wait sit far in the future, thus a fire never reaches them. The arm that
    // fires sets its timer at the clock. It then moves the clock past the timer by a step that
    // cannot reach the waiting timers in any run.
    private const double WaitingFrom = 1_000_000;

    private readonly List<ITimer> waiting = [];

    private Clock clock = new();

    private Action countFire = Nothing;

    private int fired;

    private double now;

    /// <summary>How many timers wait in the set while the arms run.</summary>
    [Params(10, 1_000)]
    public int Pending { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Makes the clock, fills its set with the timers that wait, and refuses to
    ///     run if the fire arm does not fire its timer.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.clock = new Clock();
        this.now = 0;
        this.fired = 0;
        this.countFire = () => this.fired++;

        for (int index = 0; index < this.Pending; index++)
        {
            this.waiting.Add(this.clock.SetTimer(time: WaitingFrom + index, callback: Nothing));
        }

        this.SetThenFire();

        if (this.fired != 1)
        {
            throw new InvalidOperationException(
                $"The fire arm fired {this.fired} timers and not one, so the clock did not reach the timer it set.");
        }
    }

    /// <summary>Cancels the timers that wait.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (ITimer timer in this.waiting)
        {
            timer.Cancel();
        }

        this.waiting.Clear();
    }

    /// <summary>A timer set in the middle of the waiting timers, and then canceled.</summary>
    [Benchmark(Description = "set a timer, then cancel it", Baseline = true)]
    public void SetThenCancel() =>
        this.clock.SetTimer(time: WaitingFrom + this.Pending / 2.0, callback: Nothing).Cancel();

    /// <summary>A timer set at the clock, and then fired by a move of the clock past it.</summary>
    [Benchmark(Description = "set a timer, then fire it")]
    public void SetThenFire()
    {
        this.clock.SetTimer(time: this.now, callback: this.countFire);
        this.now += 1e-6;
        this.clock.Current = this.now;
        this.clock.RunTimersTo(this.now);
    }

    /// <summary>A clock that the arms move by hand, with no thread.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class Clock : TimerSystemImplementationBase<double>
    {
        internal double Current { get; set; }

        public override double Now => this.Current;

        protected override TimeSpan SubtractTimes(double first, double second) => TimeSpan.FromSeconds(first - second);
    }
}
