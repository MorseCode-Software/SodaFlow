using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using SodaFlow.Time;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using ITimer = SodaFlow.Time.ITimer;

namespace SodaFlow.Tests;

public sealed class TimerTests
{
    private IDisposable? timerSystem;

    /// <summary>Disposes the timer system of the test, which removes its transaction hook.</summary>
    [After(HookType.Test)]
    public void Cleanup() => this.timerSystem?.Dispose();

    [Test]
    public async Task SimultaneousTimerEvents()
    {
        TimerSystem<DateTime> ts =
            this.Keep(
                new SystemClockTimerSystem(static _ =>
                {
                }));

        Behavior<DateTime> time = ts.Time;
        List<DateTime> l = [];

        Transaction.RunVoid(() =>
        {
            DateTime now = time.Sample();
            Stream<DateTime> a1 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(99))));
            Stream<DateTime> a2 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
            Stream<DateTime> a3 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
            Stream<DateTime> m = a1.OrElse(a2).OrElse(a3);

            m.ListenStrong(v =>
            {
                lock (l)
                {
                    l.Add(v);
                }
            });
        });

        // This waits for the alarms, and does not use a constant window. The alarms are 99ms and
        // 100ms in the future. Thus, a constant sleep of 200ms left approximately 100ms of margin,
        // and a CI agent with a high load used more than that margin. The run then gave zero events,
        // and not an incorrect count. The cause was a late timer thread, and not an error in the
        // coalesce. A wait on the condition makes a slow machine use more time, and it does not
        // fail.
        //
        // The wait after this is what keeps the assertion useful. The count must be two. Thus, the
        // test sees a third firing when a2 and a3 do not coalesce, and does not run before it.
        //
        // The lock is necessary, because the timer thread writes l and this code reads it. The
        // initial test with a constant sleep had no lock.
        SpinWait.SpinUntil(
            condition: () =>
            {
                lock (l)
                {
                    return l.Count >= 2;
                }
            },
            timeout: TimeSpan.FromSeconds(10));

        Thread.Sleep(100);

        int count;

        // Read with the lock held, and asserted out of it. A lock body permits no await, and the lock
        // is here to make the read safe, and not the assertion.
        lock (l)
        {
            count = l.Count;
        }

        await Assert.That(count).IsEqualTo(2);
    }

    // The clock fires the due timers from the latest to the earliest. Two threads that fire timers at
    // the same time can give that sequence too.
    [Test]
    public async Task EventsGetToTheGraphInTheSequenceOfTheirTimes()
    {
        ScriptedClock clock = new() { FireLatestFirst = true };
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        List<int> times = [];
        List<int> alarms = [];

        IStrongListener l =
            Transaction.Run(() =>
                Listener.CreateStrongComposite(
                [
                    Operational.Updates(timers.Time).ListenStrong(times.Add),
                    timers.At(Cell.Constant(Maybe.Some(10))).ListenStrong(alarms.Add),
                    timers.At(Cell.Constant(Maybe.Some(11))).ListenStrong(alarms.Add)
                ]));

        clock.AdvanceTo(12);
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [10, 11], ordering: CollectionOrdering.Matching);
        await Assert.That(times).IsEquivalentTo(expected: [10, 11, 12], ordering: CollectionOrdering.Matching);
    }

    // The clock keeps the timer back, as an implementation does between the removal of a timer and
    // its callback. A transaction then sends 12, and the event for 10 arrives after it.
    [Test]
    public async Task AnEventThatArrivesLateFiresItsAlarmAndDoesNotMoveTimeBack()
    {
        ScriptedClock clock = new() { HoldBack = Maybe.Some(10) };
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        List<int> times = [];
        List<int> alarms = [];

        IStrongListener l =
            Transaction.Run(() =>
                Listener.CreateStrongComposite(
                [
                    Operational.Updates(timers.Time).ListenStrong(times.Add),
                    timers.At(Cell.Constant(Maybe.Some(10))).ListenStrong(alarms.Add)
                ]));

        clock.AdvanceTo(12);
        clock.FireHeldBackTimer();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching);
        await Assert.That(times).IsEquivalentTo(expected: [12, 12], ordering: CollectionOrdering.Matching);
    }

    // A disposed timer system removes its hook and stops its clock. Thus, Time keeps the last value
    // it had, and an alarm that comes due after the disposal does not fire.
    [Test]
    public async Task ADisposedTimerSystemStopsItsClockAndSendsNoAlarm()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = clock.CreateTimerSystem();
        List<int> alarms = [];

        using IStrongListener l = timers.At(Cell.Constant(Maybe.Some(10))).ListenStrong(alarms.Add);

        clock.AdvanceTo(5);
        timers.Dispose();
        clock.AdvanceTo(12);

        await Assert.That(clock.Stopped).IsTrue();
        await Assert.That(timers.Time.Sample()).IsEqualTo(5);
        await Assert.That(alarms).IsEmpty();

        // A second disposal does nothing.
        timers.Dispose();
    }

    // Stop ends the timer thread and waits for it. Thus, a timer that is due after the stop does not
    // fire: not on that thread, and not from RunTimersTo. The wait after the timer is where a live
    // thread fires it, in a few milliseconds after the signal.
    [Test]
    public async Task AStoppedImplementationFiresNoTimer()
    {
        StopwatchClock clock = new();
        bool fired = false;

        clock.Start(static _ =>
        {
        });

        clock.Stop();

        using ITimer timer = clock.SetTimer(time: clock.Now - 1, callback: () => fired = true);

        clock.RunTimersTo(clock.Now);
        await Task.Delay(200);

        await Assert.That(fired).IsFalse();

        // A second stop does nothing.
        clock.Stop();
    }

    // The clock is the authority for the current time. Thus, Time follows a clock that goes back, for
    // example at the end of daylight saving time.
    [Test]
    public async Task TimeFollowsAClockThatGoesBack()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        List<int> times = [];

        IStrongListener l = Operational.Updates(timers.Time).ListenStrong(times.Add);
        clock.AdvanceTo(12);
        clock.AdvanceTo(5);
        l.Unlisten();

        await Assert.That(times).IsEquivalentTo(expected: [12, 5], ordering: CollectionOrdering.Matching);
    }

    /// <summary>Gives the timer system of this test to the cleanup hook, and returns it.</summary>
    private T Keep<T>(T timers)
        where T : IDisposable
    {
        this.timerSystem = timers;
        return timers;
    }

    // A clock on the base, with the thread of the base, for the tests of that thread.
    private sealed class StopwatchClock : TimerSystemImplementationBase<double>
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public override double Now => this.stopwatch.Elapsed.TotalSeconds;

        protected override TimeSpan SubtractTimes(double first, double second) => TimeSpan.FromSeconds(first - second);
    }

    // A clock that a test moves, which fires its timers in a sequence that the test selects.
    private sealed class ScriptedClock : ITimerSystemImplementation<int>
    {
        private readonly List<ScriptedTimer> timers = [];

        public int Now { get; private set; }

        /// <summary>Gets a value that is true when RunTimersTo fires the latest timer first.</summary>
        internal bool FireLatestFirst { get; init; }

        /// <summary>Gets the time of a timer that RunTimersTo does not fire, or None.</summary>
        internal Maybe<int> HoldBack { get; init; } = Maybe.None;

        /// <summary>Gets a value that is true after Stop.</summary>
        internal bool Stopped { get; private set; }

        public void Start(Action<Exception> handleException)
        {
        }

        public void Stop() => this.Stopped = true;

        public ITimer SetTimer(int time, Action callback)
        {
            ScriptedTimer timer = new(time: time, callback: callback);
            this.timers.Add(timer);
            return timer;
        }

        public void RunTimersTo(int now)
        {
            ScriptedTimer[] due =
            [
                .. this.timers.Where(timer =>
                    !timer.Canceled && timer.Time <= now && !this.HoldBack.Equals(Maybe.Some(timer.Time)))
            ];

            if (this.FireLatestFirst)
            {
                Array.Reverse(due);
            }

            foreach (ScriptedTimer timer in due)
            {
                this.timers.Remove(timer);
                timer.Fire();
            }
        }

        internal TimerSystem<int> CreateTimerSystem() =>
            new(
                implementation: this,
                handleException: static _ =>
                {
                });

        /// <summary>
        ///     Moves the clock, and opens a transaction. The timer system reads the clock
        ///     at the start of a transaction.
        /// </summary>
        internal void AdvanceTo(int now)
        {
            this.Now = now;

            Transaction.RunVoid(static () =>
            {
            });
        }

        /// <summary>Fires the timer that RunTimersTo kept back, out of any transaction.</summary>
        internal void FireHeldBackTimer()
        {
            ScriptedTimer timer = this.timers.Single(t => this.HoldBack.Equals(Maybe.Some(t.Time)));
            this.timers.Remove(timer);
            timer.Fire();
        }

        private sealed class ScriptedTimer(int time, Action callback) : ITimer
        {
            // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed
            // so callback is not captured into a mutable variable.
            private readonly Action callback = callback;

            public int Time { get; } = time;

            public bool Canceled { get; private set; }

            public void Cancel() => this.Canceled = true;

            public void Dispose() => this.Canceled = true;

            internal void Fire() => this.callback();
        }
    }
}
