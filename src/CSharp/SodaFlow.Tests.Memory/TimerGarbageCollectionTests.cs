using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SodaFlow.Functional;
using SodaFlow.Time;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Memory;

/// <summary>
///     A lifetime test over the timer system.
/// </summary>
/// <remarks>
///     <para>
///         TimerSystem.At builds a stream that no caller connects. It makes an alarm sink, listens
///         to the cell it gets, and gives the caller that sink alone. No other code can get to
///         the parts that make the sink fire. Thus a collection is the event that can break it.
///     </para>
///     <para>
///         This does not measure the listener that At attaches to the alarm. I removed that
///         attached listener and the test did not fail. Two conditions cause that, and the two are
///         worth a record. The listener is strong, thus the graph of the cell keeps it alive while
///         a reference to some part above the cell stays. The clock also keeps the alarm, because
///         the function that At gives to SetTimer holds it. Where no reference above the cell
///         stays, the cell cannot change, and the listener has no more work. Thus think of the
///         attached listener as a second safeguard until a test shows more.
///     </para>
/// </remarks>
public sealed class TimerGarbageCollectionTests
{
    private readonly ManualImplementation implementation = new();
    private readonly TimerSystem<int> timers;

    public TimerGarbageCollectionTests() =>
        this.timers =
            new TimerSystem<int>(implementation: this.implementation, handleException: static _ =>
            {
            });

    [Test]
    public async Task AnAlarmStillFiresAfterACollectionTakesEverythingButTheStream()
    {
        List<int> @out = [];

        // Keeps what a caller keeps and no more: the stream At returns. No code here can get to
        // the cell, or to the listener that At made over it.
        Stream<int> alarm = this.CreateAlarmAndDropTheCell(10);

        using (alarm.ListenStrong(@out.Add))
        {
            // This is a test only if an alarm was set. Without this line it also succeeds
            // where At does nothing at all.
            await Assert.That(this.implementation.SetTimerCount)
                .IsEqualTo(1)
                .Because("At should set one timer for the time the cell holds");

            Collect();

            // The negative half: no alarm comes before the clock gets to it.
            Transaction.RunVoid(static () =>
            {
            });

            await Assert.That(@out).IsEmpty().Because("the clock did not get to the alarm yet");

            this.implementation.AdvanceTo(10);

            Transaction.RunVoid(static () =>
            {
            });

            await Assert.That(@out)
                .IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching)
                .Because("the alarm should fire when a collection takes all but the stream");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Stream<int> CreateAlarmAndDropTheCell(int time)
    {
        Cell<Maybe<int>> cell = Cell.Constant(Maybe.Some(time));
        return Transaction.Run(() => this.timers.At(cell));
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // A clock the test moves by hand, so an alarm is a step and not a wait. The other test over
    // the timer system runs on the system clock and waits for milliseconds. A collection cannot
    // mix with that, because this measures what a reference holds at one moment.
    private sealed class ManualImplementation : ITimerSystemImplementation<int>
    {
        private readonly List<ManualTimer> timers = [];

        public int Now { get; private set; }

        public int SetTimerCount { get; private set; }

        public void Start(Action<Exception> handleException)
        {
        }

        public ITimer SetTimer(int time, Action callback)
        {
            this.SetTimerCount++;
            ManualTimer timer = new(time: time, callback: callback);
            this.timers.Add(timer);
            return timer;
        }

        public void RunTimersTo(int now)
        {
            // A copy, because a function that runs here can set a timer of its own.
            ManualTimer[] due = [.. this.timers.Where(timer => !timer.Canceled && timer.Time <= now)];

            foreach (ManualTimer timer in due)
            {
                this.timers.Remove(timer);
                timer.Fire();
            }
        }

        public void AdvanceTo(int now) => this.Now = now;

        private sealed class ManualTimer(int time, Action callback) : ITimer
        {
            // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed
            // so callback is not captured into a mutable variable.
            private readonly Action callback = callback;

            public int Time { get; } = time;

            public bool Canceled { get; private set; }

            public void Cancel() => this.Canceled = true;

            public void Dispose() => this.Cancel();

            public void Fire() => this.callback();
        }
    }
}
