using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Time;

/// <summary>
///     A timer system built on an <see cref="ITimerSystemImplementation{T}" />, which supplies the
///     clock and the wait mechanism. This class gives the FRP.
/// </summary>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Alarms get to the graph through a <c>Transaction.OnStart</c> handler installed by the
///     constructor. When a transaction starts, the handler reads the current time, runs any timer that
///     has come due, and sends the alarms. The handler sends events that became due at the same time
///     delivered together, and events at different times in different transactions.
///     Use <see cref="SystemClockTimerSystem" /> or <see cref="SecondsTimerSystem" /> unless you
///     a different clock is necessary, and then derive from
///     <see cref="TimerSystemImplementationBase{T}" /> and give it here.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class TimerSystem<T> : ITimerSystem<T>
    where T : IComparable<T>
{
    private readonly Queue<Event> eventQueue = new();
    private readonly ITimerSystemImplementation<T> implementation;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TimerSystem{T}" /> class over the given
    ///     implementation, starting it immediately.
    /// </summary>
    /// <param name="implementation">The clock and waiting mechanism to build on.</param>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that fires.
    /// </param>
    /// <remarks>
    ///     Construction starts the implementation and installs a transaction handler that lives for
    ///     the lifetime of the process. Thus, make one timer system for the process, and not one for
    ///     each unit of work.
    /// </remarks>
    public TimerSystem(ITimerSystemImplementation<T> implementation, Action<Exception> handleException)
    {
        this.implementation = implementation;
        this.implementation.Start(handleException);
        BehaviorSink<T> timeSink = new(this.implementation.Now);
        this.Time = timeSink;

        Transaction.OnStart(() =>
        {
            T t = this.implementation.Now;
            this.implementation.RunTimersTo(t);
            List<Event> events = [];

            while (true)
            {
                // Pop all events earlier than t.
                lock (this.eventQueue)
                {
                    if (this.eventQueue.Count > 0)
                    {
                        Event tempEvent = this.eventQueue.Peek();

                        if (tempEvent.Time.CompareTo(t) <= 0)
                        {
                            events.Add(this.eventQueue.Dequeue());

                            T timeToCheck = tempEvent.Time;

                            while (this.eventQueue.Count > 0)
                            {
                                tempEvent = this.eventQueue.Peek();

                                if (tempEvent.Time.CompareTo(timeToCheck) == 0)
                                {
                                    events.Add(this.eventQueue.Dequeue());
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                if (events.Count > 0)
                {
                    timeSink.Send(events[0].Time);

                    Transaction.RunVoid(() =>
                    {
                        foreach (Event ev in events)
                        {
                            ev.Alarm.Send(ev.Time);
                        }
                    });
                }
                else
                {
                    break;
                }

                events.Clear();
            }

            timeSink.Send(t);
        });
    }

    /// <inheritdoc />
    public Behavior<T> Time { get; }

    /// <inheritdoc />
    public Stream<T> At(Cell<Maybe<T>> t)
    {
        StreamSink<T> alarm = new();
        Maybe<ITimer> currentTimer = Maybe.None;

        IListener l =
            t.ListenStrong(m =>
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
                                            this.eventQueue.Enqueue(new Event(time: time, alarm: alarm));
                                        }

                                        // Open and close a transaction to cause queued
                                        // events to run.
                                        Transaction.RunVoid(static () =>
                                        {
                                        });
                                    })),
                        onNone: static () => Maybe.None);
            });

        return alarm.AttachListener(l);
    }

    private class Event
    {
        internal readonly StreamSink<T> Alarm;

        internal readonly T Time;

        internal Event(T time, StreamSink<T> alarm)
        {
            this.Time = time;
            this.Alarm = alarm;
        }
    }
}
