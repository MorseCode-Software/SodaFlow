using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Time;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

/// <summary>
///     Debounce, on a clock that each test moves by hand. A test of a wait on the
///     true clock measures the machine and not the graph.
/// </summary>
public sealed class DebounceTests
{
    [Test]
    public async Task ASequenceOfFiringsWithNoSpaceGivesTheLastValueOneTime()
    {
        ManualClock clock = new();
        StreamSink<string> keys = Stream.CreateSink<string>();
        List<string> seen = [];
        IStrongListener l = Transaction.Run(() => Debounced(keys: keys, clock: clock).ListenStrong(seen.Add));

        keys.Send("a");
        clock.AdvanceTo(3);
        keys.Send("ab");
        clock.AdvanceTo(6);
        keys.Send("abc");

        await Assert.That(seen).IsEmpty().Because("each firing moves the alarm out");

        // The last firing was at 6, thus the alarm is at 16.
        clock.AdvanceTo(16);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["abc"], ordering: CollectionOrdering.Matching)
            .Because("the last value fires one time, after the quiet");

        l.Unlisten();
    }

    [Test]
    public async Task TheAlarmDoesNotFireASecondTime()
    {
        ManualClock clock = new();
        StreamSink<string> keys = Stream.CreateSink<string>();
        List<string> seen = [];
        IStrongListener l = Transaction.Run(() => Debounced(keys: keys, clock: clock).ListenStrong(seen.Add));

        keys.Send("a");
        clock.AdvanceTo(10);

        await Assert.That(seen.Count).IsEqualTo(1);

        // The alarm clears its own deadline. Also, the timer system sets a timer at each change of
        // the deadline, thus a deadline that stays gives one alarm and not two.
        clock.AdvanceTo(1000);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["a"], ordering: CollectionOrdering.Matching)
            .Because("an alarm that fired holds no deadline");

        l.Unlisten();
    }

    [Test]
    public async Task AFiringAfterTheQuietArmsTheAlarmAgain()
    {
        ManualClock clock = new();
        StreamSink<string> keys = Stream.CreateSink<string>();
        List<string> seen = [];
        IStrongListener l = Transaction.Run(() => Debounced(keys: keys, clock: clock).ListenStrong(seen.Add));

        keys.Send("a");
        clock.AdvanceTo(10);

        keys.Send("b");
        clock.AdvanceTo(20);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["a", "b"], ordering: CollectionOrdering.Matching)
            .Because("the debounce works again after it fired");

        l.Unlisten();
    }

    [Test]
    public async Task ASpaceBelowTheQuietTimeGivesOneValueAndNotTwo()
    {
        ManualClock clock = new();
        StreamSink<string> keys = Stream.CreateSink<string>();
        List<string> seen = [];
        IStrongListener l = Transaction.Run(() => Debounced(keys: keys, clock: clock).ListenStrong(seen.Add));

        keys.Send("a");

        // A step of the clock with no firing, and below the quiet time. The alarm is at 10, thus
        // this step comes to no alarm.
        clock.AdvanceTo(9);
        await Assert.That(seen).IsEmpty();

        // This firing moves the alarm from 10 to 19.
        keys.Send("b");
        clock.AdvanceTo(10);

        await Assert.That(seen).IsEmpty().Because("the firing at 9 moved the alarm from 10 to 19");

        clock.AdvanceTo(19);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["b"], ordering: CollectionOrdering.Matching)
            .Because("the value of the last firing fires, and the value before it never fires");

        l.Unlisten();
    }

    [Test]
    public async Task TheResultIsNotInTheTransactionOfAFiring()
    {
        ManualClock clock = new();
        StreamSink<string> keys = Stream.CreateSink<string>();
        List<string> seen = [];

        Stream<string> debounced = Transaction.Run(() => Debounced(keys: keys, clock: clock));

        // A listener that reads the source in the transaction of the result. A result in the
        // transaction of a firing reads the value of that same firing.
        Cell<string> lastKey = Transaction.Run(() => keys.Hold(string.Empty));

        IStrongListener l =
            Transaction.Run(() =>
                debounced
                    .Snapshot(c: lastKey, f: static (value, held) => value + " after " + held)
                    .ListenStrong(seen.Add));

        keys.Send("a");
        clock.AdvanceTo(10);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["a after a"], ordering: CollectionOrdering.Matching)
            .Because("the cell of the source has the value of the firing before the alarm fires");

        l.Unlisten();
    }

    [Test]
    public async Task AFiringInTheTransactionOfTheAlarmArmsTheAlarmAgain()
    {
        ManualClock clock = new();
        StreamSink<string> keys = Stream.CreateSink<string>();
        List<string> seen = [];

        // The source of the debounce is the sink of this test and an echo of the result. Thus, the
        // value "a" that the alarm sends makes a firing of "b" in the transaction of that alarm.
        // That transaction is the one position where the two sides of the deadline are together.
        Stream<string> debounced =
            Transaction.Run(() =>
                Stream.Loop<string>()
                    .WithCaptures(source =>
                    {
                        Stream<string> result =
                            source.AsStream()
                                .Debounce(timers: clock.Timers, deadline: static now => now + 10);

                        Stream<string> echo = result.Filter(static value => value == "a").MapTo("b");

                        return (Stream: keys.OrElse(echo), Captures: result);
                    })
                    .Captures);

        IStrongListener l = Transaction.Run(() => debounced.ListenStrong(seen.Add));

        keys.Send("a");
        clock.AdvanceTo(10);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["a"], ordering: CollectionOrdering.Matching)
            .Because("the first value fires after its quiet time");

        // The echo of "a" armed the alarm again, at 10 + 10. A deadline that the alarm cleared in
        // that same transaction loses "b", because nothing arms it after that.
        clock.AdvanceTo(20);

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["a", "b"], ordering: CollectionOrdering.Matching)
            .Because("a firing that shares the transaction of the alarm arms the alarm again");

        l.Unlisten();
    }

    private static Stream<string> Debounced(Stream<string> keys, ManualClock clock) =>
        keys.Debounce(timers: clock.Timers, deadline: static now => now + 10);

    /// <summary>A clock that a test moves, with the timer system over it.</summary>
    private sealed class ManualClock
    {
        private readonly Implementation implementation;

        internal ManualClock()
        {
            this.implementation = new Implementation();

            this.Timers =
                new TimerSystem<int>(
                    implementation: this.implementation,
                    handleException: static _ =>
                    {
                    });
        }

        internal ITimerSystem<int> Timers { get; }

        /// <summary>
        ///     Moves the clock, and opens a transaction. The timer system reads the clock
        ///     at the start of a transaction, thus a step with no transaction sends no
        ///     alarm.
        /// </summary>
        internal void AdvanceTo(int now)
        {
            this.implementation.AdvanceTo(now);

            Transaction.RunVoid(static () =>
            {
            });
        }

        private sealed class Implementation : ITimerSystemImplementation<int>
        {
            private readonly List<ManualTimer> timers = [];

            public int Now { get; private set; }

            public void Start(Action<Exception> handleException)
            {
            }

            public ITimer SetTimer(int time, Action callback)
            {
                ManualTimer timer = new(time: time, callback: callback);
                this.timers.Add(timer);

                return timer;
            }

            public void RunTimersTo(int now)
            {
                // A copy, because a callback here can set a timer of its own.
                ManualTimer[] due = [.. this.timers.Where(timer => !timer.Canceled && timer.Time <= now)];

                foreach (ManualTimer timer in due)
                {
                    this.timers.Remove(timer);
                    timer.Fire();
                }
            }

            internal void AdvanceTo(int now) => this.Now = now;

            private sealed class ManualTimer(int time, Action callback) : ITimer
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
}
