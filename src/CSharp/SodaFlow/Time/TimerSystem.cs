using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Time;

/// <summary>
///     A timer system built on an <see cref="ITimerSystemImplementation{T}" />,
///     which supplies the clock and the wait mechanism. This class gives the FRP.
/// </summary>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Alarms get to the graph through a <c>Transaction.OnStart</c> handler
///     installed by the constructor. When a transaction starts, the handler reads
///     the current time, runs any timer that has come due, and sends the alarms.
///     The handler sends events that became due at the same time delivered
///     together, and events at different times in different transactions.
///     Use <see cref="SystemClockTimerSystem" /> or
///     <see cref="SecondsTimerSystem" /> unless you a different clock is
///     necessary, and then derive from
///     <see cref="TimerSystemImplementationBase{T}" /> and give it here.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class TimerSystem<T> : ITimerSystem<T>
    where T : IComparable<T>
{
    // In the sequence of the time of each event, and of its arrival for one time. An implementation
    // runs a callback out of its lock, and it can run timers on more than one thread. Thus, the
    // events can get to this set in a different sequence from their times.
    private readonly SortedSet<Event> eventQueue =
        new(
            Comparer<Event>.Create(static (x, y) =>
            {
                int timeComparison = x.Time.CompareTo(y.Time);
                return timeComparison != 0 ? timeComparison : x.Seq.CompareTo(y.Seq);
            }));

    private readonly ITimerSystemImplementation<T> implementation;

    // The last value that the OnStart handler sent to Time. Only that handler uses this, and it
    // runs with the transaction lock.
    private T lastTime;

    // The arrival number of the next event. Only code that holds the lock on eventQueue uses this.
    private long nextEventSeq;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TimerSystem{T}" /> class over
    ///     the given implementation, starting it immediately.
    /// </summary>
    /// <param name="implementation">The clock and waiting mechanism to build on.</param>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    /// <remarks>
    ///     Construction starts the implementation and installs a transaction handler
    ///     that lives for the lifetime of the process. Thus, make one timer system for
    ///     the process, and not one for each unit of work.
    /// </remarks>
    public TimerSystem(ITimerSystemImplementation<T> implementation, Action<Exception> handleException)
    {
        this.implementation = implementation;
        this.implementation.Start(handleException);
        this.lastTime = this.implementation.Now;
        BehaviorSink<T> timeSink = new(this.lastTime);
        this.Time = timeSink;

        Transaction.OnStart(() =>
        {
            T t = this.implementation.Now;
            this.implementation.RunTimersTo(t);

            // This runs at the start of each transaction in the process, and most of them have no
            // event due. That path allocates nothing here: the list exists only when events are
            // due.
            while (this.TakeDueEvents(t) is { } events)
            {
                T eventTime = events[0].Time;

                // An implementation removes a timer and then runs its callback out of its lock. A
                // transaction can start between the two and send a time after the time of the event.
                // The event then gets to the set after that time. Its alarm fires at its own time, and
                // Time does not go back.
                if (eventTime.CompareTo(this.lastTime) >= 0)
                {
                    timeSink.Send(eventTime);
                    this.lastTime = eventTime;
                }

                Transaction.RunVoid(() =>
                {
                    foreach (Event ev in events)
                    {
                        ev.Alarm.Send(ev.Time);
                    }
                });
            }

            // The clock itself can go back, for example at the end of daylight saving time. Time
            // follows the clock then, as the clock is the authority for the current time.
            timeSink.Send(t);
            this.lastTime = t;
        });
    }

    /// <inheritdoc />
    public Behavior<T> Time { get; }

    // The earliest events at or before t, which are all at one time, removed from the set. Null
    // where no event is due. The set is in the sequence of time, thus these are its first events.
    // Count and Min are properties of the set. FirstOrDefault is the LINQ method, which boxes the
    // enumerator of the set at each call, and this runs at each start of a transaction. The LINQ
    // below runs only when events are due, which is rare.
    private List<Event>? TakeDueEvents(T t)
    {
        lock (this.eventQueue)
        {
            if (this.eventQueue.Count == 0)
            {
                return null;
            }

            // ReSharper disable once NullableWarningSuppressionIsUsed - The set holds at least one event, thus
            // Min is not null.
            Event first = this.eventQueue.Min!;

            if (first.Time.CompareTo(t) > 0)
            {
                return null;
            }

            List<Event> events = [.. this.eventQueue.TakeWhile(ev => ev.Time.CompareTo(first.Time) == 0)];

            foreach (Event ev in events)
            {
                this.eventQueue.Remove(ev);
            }

            return events;
        }
    }

    /// <inheritdoc />
    public Stream<T> At(Cell<Maybe<T>> t)
    {
        StreamSink<T> alarm = new();
        Maybe<ITimer> currentTimer = Maybe.None;

        IListener l =
            t.Listen(m =>
            {
                currentTimer.MatchSome(static timer => timer.Cancel());

                currentTimer =
                    m.Match(
                        onSome: time =>
                            Maybe.Some(
                                this.implementation.SetTimer(
                                    time: time,
                                    callback: () =>
                                    {
                                        lock (this.eventQueue)
                                        {
                                            this.eventQueue.Add(
                                                new Event(time: time, seq: this.nextEventSeq++, alarm: alarm));
                                        }

                                        // Open and close a transaction to cause queued
                                        // events to run.
                                        Transaction.RunVoid(static () =>
                                        {
                                        });
                                    })),
                        onNone: static () => Maybe.None);
            });

        return alarm.AttachListenerInternal(l);
    }

    private sealed class Event(in T time, in long seq, in StreamSink<T> alarm)
    {
        internal readonly StreamSink<T> Alarm = alarm;

        internal readonly long Seq = seq;

        internal readonly T Time = time;
    }
}
