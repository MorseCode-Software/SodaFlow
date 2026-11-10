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

        // The listener holds the alarms. Thus, the test keeps it until the end.
        using IListener listener =
            Transaction.Run(() =>
            {
                DateTime now = time.Sample();
                Stream<DateTime> a1 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(99))));
                Stream<DateTime> a2 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
                Stream<DateTime> a3 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
                Stream<DateTime> m = a1.OrElse(a2).OrElse(a3);

                return m.Listen(v =>
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

        IListener l =
            Transaction.Run(() =>
                Listener.CreateComposite(
                [
                    Operational.Updates(timers.Time).Listen(times.Add),
                    timers.At(Cell.Constant(Maybe.Some(10))).Listen(alarms.Add),
                    timers.At(Cell.Constant(Maybe.Some(11))).Listen(alarms.Add)
                ]));

        clock.AdvanceTo(12);
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [10, 11], ordering: CollectionOrdering.Matching);
        await Assert.That(times).IsEquivalentTo(expected: [10, 11, 12], ordering: CollectionOrdering.Matching);
    }

    // The clock keeps the timer back, as an implementation does between the removal of a timer and
    // its callback. A transaction then sends 12, and the event for 10 arrives after it. The
    // transaction that brings the event sends no time, because the clock reads 12, as before.
    [Test]
    public async Task AnEventThatArrivesLateFiresItsAlarmAndDoesNotMoveTimeBack()
    {
        ScriptedClock clock = new() { HoldBack = Maybe.Some(10) };
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        List<int> times = [];
        List<int> alarms = [];

        IListener l =
            Transaction.Run(() =>
                Listener.CreateComposite(
                [
                    Operational.Updates(timers.Time).Listen(times.Add),
                    timers.At(Cell.Constant(Maybe.Some(10))).Listen(alarms.Add)
                ]));

        clock.AdvanceTo(12);
        clock.FireHeldBackTimer();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching);
        await Assert.That(times).IsEquivalentTo(expected: [12], ordering: CollectionOrdering.Matching);
    }

    // A disposed timer system removes its hook and stops its clock. Thus, Time keeps the last value
    // it had, and an alarm that comes due after the disposal does not fire.
    [Test]
    public async Task ADisposedTimerSystemStopsItsClockAndSendsNoAlarm()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = clock.CreateTimerSystem();
        List<int> alarms = [];

        using IListener l = timers.At(Cell.Constant(Maybe.Some(10))).Listen(alarms.Add);

        clock.AdvanceTo(5);
        timers.Dispose();
        clock.AdvanceTo(12);

        await Assert.That(clock.Stopped).IsTrue();
        await Assert.That(timers.Time.Sample()).IsEqualTo(5);
        await Assert.That(alarms).IsEmpty();

        // A second disposal does nothing.
        timers.Dispose();
    }

    // Stop ends the timer thread and waits for it. Thus, a timer set before the stop does not fire
    // after it: not on that thread, and not from RunTimersTo. A timer set after the stop never
    // fires, and a handle for it tells the caller nothing. Thus, SetTimer throws instead.
    [Test]
    public async Task AStoppedImplementationRefusesATimerAndFiresNone()
    {
        StopwatchClock clock = new();
        bool fired = false;

        clock.Start(static _ =>
        {
        });

        using ITimer timer = clock.SetTimer(time: clock.Now + 1000, callback: () => fired = true);

        clock.Stop();

        await Assert.That(() => _ = clock.SetTimer(time: clock.Now - 1, callback: () => fired = true))
            .ThrowsExactly<ObjectDisposedException>();

        clock.RunTimersTo(clock.Now + 2000);
        await Task.Delay(200);

        await Assert.That(fired).IsFalse();

        // A second stop does nothing.
        clock.Stop();
    }

    // At sets its timer in the transaction that gives the cell a time. Thus, that transaction fails
    // with the exception of the stopped implementation. That is the construction when the cell
    // holds a time, and the later send that gives it one. A cell with no time sets nothing and
    // passes.
    [Test]
    public async Task ADisposedTimerSystemRefusesAnAlarm()
    {
        TimerSystem<double> timers =
            new(
                implementation: new StopwatchClock(),
                handleException: static _ =>
                {
                });

        timers.Dispose();

        await Assert.That(() => _ = timers.At(Cell.Constant(Maybe.Some(1.0)))).ThrowsExactly<ObjectDisposedException>();

        CellSink<Maybe<double>> alarm = Cell.CreateSink<Maybe<double>>(Maybe.None);
        Stream<double> at = timers.At(alarm);

        await Assert.That(() => alarm.Send(Maybe.Some(1.0))).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(at).IsNotNull();
    }

    // The refusal comes in the transaction, thus the full transaction fails and the cell keeps the
    // deadline it had. With a timer that At set after the commit, the cell held the new deadline
    // after the refusal, with no timer for it.
    [Test]
    public async Task ARefusedAlarmLeavesTheDeadlineAsItWas()
    {
        TimerSystem<double> timers =
            new(
                implementation: new StopwatchClock(),
                handleException: static _ =>
                {
                });

        CellSink<Maybe<double>> deadline = Cell.CreateSink<Maybe<double>>(Maybe.None);
        Stream<double> at = timers.At(deadline);

        timers.Dispose();

        await Assert.That(() => deadline.Send(Maybe.Some(1.0))).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(deadline.Sample()).IsEqualTo(Maybe<double>.None);
        await Assert.That(at).IsNotNull();
    }

    // A transaction that fails leaves the cell with the deadline it had. The timer has to stay
    // with it: the alarm fires at 10, and not at the 20 that did not commit.
    [Test]
    public async Task AFailedTransactionThatMovesADeadlineKeepsTheTimerOfTheCommittedDeadline()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        CellSink<Maybe<int>> deadline = Cell.CreateSink(Maybe.Some(10));
        List<int> alarms = [];
        IListener l = timers.At(deadline).Listen(alarms.Add);
        IListener thrower = ThrowAfterAt(deadline: deadline, refused: 20);

        await Assert.That(() => deadline.Send(Maybe.Some(20))).ThrowsExactly<InvalidOperationException>();
        await Assert.That(deadline.Sample()).IsEqualTo(Maybe.Some(10));

        clock.AdvanceTo(10);

        await Assert.That(alarms).IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching);

        clock.AdvanceTo(25);
        thrower.Unlisten();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching);
    }

    // The deadline that did not commit is the earlier one here. No alarm fires at it, and the
    // alarm of the committed deadline fires.
    [Test]
    public async Task AFailedTransactionThatMovesADeadlineEarlierSendsNoAlarmAtThatDeadline()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        CellSink<Maybe<int>> deadline = Cell.CreateSink(Maybe.Some(20));
        List<int> alarms = [];
        IListener l = timers.At(deadline).Listen(alarms.Add);
        IListener thrower = ThrowAfterAt(deadline: deadline, refused: 10);

        await Assert.That(() => deadline.Send(Maybe.Some(10))).ThrowsExactly<InvalidOperationException>();

        clock.AdvanceTo(15);

        await Assert.That(alarms).IsEmpty();

        clock.AdvanceTo(20);
        thrower.Unlisten();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [20], ordering: CollectionOrdering.Matching);
    }

    // The failure must not stop the next change. The next transaction moves the
    // deadline as usual: it cancels the timer at 10 and sets one at 30.
    [Test]
    public async Task ADeadlineMovesAfterAFailedTransactionThatMovedIt()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        CellSink<Maybe<int>> deadline = Cell.CreateSink(Maybe.Some(10));
        List<int> alarms = [];
        IListener l = timers.At(deadline).Listen(alarms.Add);
        IListener thrower = ThrowAfterAt(deadline: deadline, refused: 20);

        await Assert.That(() => deadline.Send(Maybe.Some(20))).ThrowsExactly<InvalidOperationException>();

        deadline.Send(Maybe.Some(30));
        clock.AdvanceTo(25);

        await Assert.That(alarms).IsEmpty();

        clock.AdvanceTo(30);
        thrower.Unlisten();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [30], ordering: CollectionOrdering.Matching);
    }

    // The timer thread can fire the new timer while the transaction that set it is open. The
    // event is then in the queue of the timer system when the transaction fails, and a cancel of
    // the timer is too late. The failure has to remove the event also.
    [Test]
    public async Task ATimerThatFiresInTheTransactionThatFailsSendsNoAlarm()
    {
        ScriptedClock clock = new() { HoldBack = Maybe.Some(15) };
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        CellSink<Maybe<int>> deadline = Cell.CreateSink(Maybe.Some(20));
        List<int> alarms = [];
        List<int> times = [];
        IListener l = timers.At(deadline).Listen(alarms.Add);
        IListener timeListener = Operational.Updates(timers.Time).Listen(times.Add);
        IListener thrower = ThrowAfterAt(deadline: deadline, refused: 15, beforeThrow: clock.FireHeldBackTimer);

        await Assert.That(() => deadline.Send(Maybe.Some(15))).ThrowsExactly<InvalidOperationException>();

        clock.AdvanceTo(16);

        await Assert.That(alarms).IsEmpty();
        await Assert.That(times).IsEquivalentTo(expected: [16], ordering: CollectionOrdering.Matching);

        clock.AdvanceTo(20);
        thrower.Unlisten();
        timeListener.Unlisten();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [20], ordering: CollectionOrdering.Matching);
    }

    // An implementation removes a timer and then runs its callback out of its lock. Thus, the
    // callback of the new timer can run after the failure of the transaction, which canceled the
    // timer. That callback must queue no event.
    [Test]
    public async Task ATimerThatFiresAfterTheTransactionThatFailedSendsNoAlarm()
    {
        ScriptedClock clock = new() { HoldBack = Maybe.Some(15) };
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        CellSink<Maybe<int>> deadline = Cell.CreateSink(Maybe.Some(20));
        List<int> alarms = [];
        IListener l = timers.At(deadline).Listen(alarms.Add);
        IListener thrower = ThrowAfterAt(deadline: deadline, refused: 15);

        await Assert.That(() => deadline.Send(Maybe.Some(15))).ThrowsExactly<InvalidOperationException>();

        clock.FireHeldBackTimer();
        clock.AdvanceTo(16);

        await Assert.That(alarms).IsEmpty();

        clock.AdvanceTo(20);
        thrower.Unlisten();
        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [20], ordering: CollectionOrdering.Matching);
    }

    // RunTimersTo fires a due timer on the calling thread, and it fires nothing after a cancel of
    // the timer. The flag that lets it skip the lock in a transaction with no timer set has to
    // follow the set through the two.
    [Test]
    public async Task RunTimersToFiresADueTimerAndNotACanceledOne()
    {
        StopwatchClock clock = new();
        int fired = 0;

        ITimer first = clock.SetTimer(time: clock.Now - 1, callback: () => fired++);
        clock.RunTimersTo(clock.Now);

        await Assert.That(fired).IsEqualTo(1);

        ITimer second = clock.SetTimer(time: clock.Now - 1, callback: () => fired++);
        second.Cancel();
        clock.RunTimersTo(clock.Now);

        await Assert.That(fired).IsEqualTo(1);

        first.Dispose();
        second.Dispose();
    }

    // The clock is the authority for the current time. Thus, Time follows a clock that goes back, for
    // example at the end of daylight saving time.
    [Test]
    public async Task TimeFollowsAClockThatGoesBack()
    {
        ScriptedClock clock = new();
        TimerSystem<int> timers = this.Keep(clock.CreateTimerSystem());
        List<int> times = [];

        IListener l = Operational.Updates(timers.Time).Listen(times.Add);
        clock.AdvanceTo(12);
        clock.AdvanceTo(5);
        l.Unlisten();

        await Assert.That(times).IsEquivalentTo(expected: [12, 5], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The system clock timer system takes any clock. Time follows that clock, and
    ///     a transaction that starts after the clock passes a timer fires it, on the
    ///     thread of that transaction.
    /// </summary>
    [Test]
    public async Task TheSystemClockTimerSystemTakesItsClock()
    {
        HandClock clock =
            new()
            {
                Now = new DateTime(year: 2026, month: 3, day: 29, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc)
            };

        DateTime start = clock.Now;

        SystemClockTimerSystem timers =
            this.Keep(
                new SystemClockTimerSystem(
                    now: () => clock.Now,
                    handleException: static _ =>
                    {
                    }));

        List<DateTime> alarms = [];

        IListener l =
            timers.At(Cell.Constant(Maybe.Some(start.AddMinutes(5)))).Listen(alarms.Add);

        Transaction.RunVoid(static () =>
        {
        });

        await Assert.That(timers.Time.Sample()).IsEqualTo(start);
        await Assert.That(alarms).IsEmpty();

        clock.Now = start.AddMinutes(5);

        Transaction.RunVoid(static () =>
        {
        });

        l.Unlisten();

        await Assert.That(timers.Time.Sample()).IsEqualTo(start.AddMinutes(5));

        await Assert.That(alarms)
            .IsEquivalentTo(expected: [start.AddMinutes(5)], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The UTC timer system measures time in UTC, and the default system clock
    ///     timer system in local time. Each value of Time says which.
    /// </summary>
    [Test]
    public async Task TheUtcClockTimerSystemCountsInUtcAndTheSystemClockOneInLocalTime()
    {
        using UtcClockTimerSystem utc = new(static _ =>
        {
        });

        using SystemClockTimerSystem local = new(static _ =>
        {
        });

        Transaction.RunVoid(static () =>
        {
        });

        await Assert.That(utc.Time.Sample().Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(local.Time.Sample().Kind).IsEqualTo(DateTimeKind.Local);
    }

    /// <summary>Gives the timer system of this test to the cleanup hook, and returns it.</summary>
    private T Keep<T>(T timers)
        where T : IDisposable
    {
        this.timerSystem = timers;
        return timers;
    }

    // A DateTime that a test moves by hand, for the clock constructor of SystemClockTimerSystem.
    // A clock with a resolution gives its value in steps. Time moves at the first transaction in
    // each step and holds its value through the others, so the updates of Time are one per step.
    [Test]
    public async Task AClockWithAResolutionMovesTimeOncePerStep()
    {
        DateTime start = new(year: 2026, month: 11, day: 6, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc);
        HandClock clock = new() { Now = start };

        SystemClockTimerSystem timers =
            this.Keep(
                new SystemClockTimerSystem(
                    now: () => clock.Now,
                    resolution: TimeSpan.FromMilliseconds(1),
                    handleException: static _ =>
                    {
                    }));

        List<DateTime> times = [];
        IListener l = Operational.Updates(timers.Time).Listen(times.Add);

        for (int step = 1; step <= 5; step++)
        {
            clock.Now = start.AddTicks(step * 4 * TimeSpan.TicksPerMillisecond / 10);

            Transaction.RunVoid(static () =>
            {
            });
        }

        l.Unlisten();

        await Assert.That(times)
            .IsEquivalentTo(
                expected: [start.AddMilliseconds(1), start.AddMilliseconds(2)],
                ordering: CollectionOrdering.Matching);

        await Assert.That(timers.Time.Sample()).IsEqualTo(start.AddMilliseconds(2));
    }

    // The value rounds down. Thus, an alarm in a step fires in the first transaction whose value
    // gets to the next step, with its own time. The step is an hour and the alarm is half an hour after
    // the start. Thus, the timer thread, which waits for the alarm on the same clock, sleeps
    // through the test, and the test's own transaction fires the alarm.
    [Test]
    public async Task AnAlarmUnderAResolutionFiresInTheFirstTransactionThatReachesIt()
    {
        DateTime start = new(year: 2026, month: 11, day: 6, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc);
        HandClock clock = new() { Now = start };

        SystemClockTimerSystem timers =
            this.Keep(
                new SystemClockTimerSystem(
                    now: () => clock.Now,
                    resolution: TimeSpan.FromHours(1),
                    handleException: static _ =>
                    {
                    }));

        DateTime alarmTime = start.AddMinutes(30);
        List<DateTime> alarms = [];
        IListener l = timers.At(Cell.Constant(Maybe.Some(alarmTime))).Listen(alarms.Add);

        clock.Now = start.AddMinutes(45);

        Transaction.RunVoid(static () =>
        {
        });

        await Assert.That(alarms).IsEmpty();

        clock.Now = start.AddMinutes(70);

        Transaction.RunVoid(static () =>
        {
        });

        l.Unlisten();

        await Assert.That(alarms).IsEquivalentTo(expected: [alarmTime], ordering: CollectionOrdering.Matching);
        await Assert.That(timers.Time.Sample()).IsEqualTo(start.AddHours(1));
    }

    // Zero is the value as it is, which is what the constructors without a resolution give.
    [Test]
    public async Task AZeroResolutionReadsTheClockAsItIs()
    {
        DateTime start = new(year: 2026, month: 11, day: 6, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc);
        HandClock clock = new() { Now = start.AddTicks(7) };

        SystemClockTimerSystem timers =
            this.Keep(
                new SystemClockTimerSystem(
                    now: () => clock.Now,
                    resolution: TimeSpan.Zero,
                    handleException: static _ =>
                    {
                    }));

        await Assert.That(timers.Time.Sample()).IsEqualTo(start.AddTicks(7));
    }

    [Test]
    public async Task ANegativeResolutionIsRefused()
    {
        TimeSpan negative = TimeSpan.FromTicks(-1);

        await Assert.That(() => this.Keep(new SecondsTimerSystem(resolution: negative, handleException: Ignore)))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(() => this.Keep(new SystemClockTimerSystem(resolution: negative, handleException: Ignore)))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(() => this.Keep(new UtcClockTimerSystem(resolution: negative, handleException: Ignore)))
            .Throws<ArgumentOutOfRangeException>();
    }

    // The seconds clock rounds in the ticks of its stopwatch. A resolution of one hour holds zero
    // for the length of a test, and the value as it is moves in a few milliseconds.
    [Test]
    public async Task TheSecondsClockRoundsItsStopwatch()
    {
        SecondsTimerSystem hourly =
            this.Keep(new SecondsTimerSystem(resolution: TimeSpan.FromHours(1), handleException: Ignore));

        SecondsTimerSystem raw = this.Keep(new SecondsTimerSystem(Ignore));

        Thread.Sleep(5);

        Transaction.RunVoid(static () =>
        {
        });

        await Assert.That(hourly.Time.Sample()).IsEqualTo(0.0);
        await Assert.That(raw.Time.Sample()).IsGreaterThan(0.0);
    }

    private static void Ignore(Exception e)
    {
    }

    // A listener that fails the transaction that sends refused to the deadline. The two maps put
    // it after the listener of At in that transaction, which has thus set its timer before the
    // throw. The sequence of listeners at one rank is not a contract, and a listener directly on
    // the cell ran first.
    private static IListener ThrowAfterAt(Cell<Maybe<int>> deadline, int refused) =>
        ThrowAfterAt(
            deadline: deadline,
            refused: refused,
            beforeThrow: static () =>
            {
            });

    // The same, with an action that runs in the transaction before the throw.
    private static IListener ThrowAfterAt(Cell<Maybe<int>> deadline, int refused, Action beforeThrow) =>
        deadline.Updates()
            .Map(static d => d)
            .Map(static d => d)
            .Listen(d =>
            {
                if (!d.Equals(Maybe.Some(refused)))
                {
                    return;
                }

                beforeThrow();

                throw new InvalidOperationException("The test fails this transaction.");
            });

    private sealed class HandClock
    {
        internal DateTime Now { get; set; }
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
