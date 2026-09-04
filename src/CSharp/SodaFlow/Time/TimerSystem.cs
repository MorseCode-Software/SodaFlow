using System;
using System.Collections.Generic;
using SodaFlow.Functional;

namespace SodaFlow.Time
{
    /// <summary>
    ///     A timer system built on an <see cref="ITimerSystemImplementation{T}" />, which supplies the
    ///     clock and the waiting; this class supplies the FRP.
    /// </summary>
    /// <typeparam name="T">The type used to express a point in time.</typeparam>
    /// <remarks>
    ///     Alarms reach the graph through a <c>Transaction.OnStart</c> hook installed by the
    ///     constructor. When a transaction starts, the hook reads the current time, runs any timer that
    ///     has come due, and sends the resulting alarms; events which came due at the same time are
    ///     delivered together, and events at different times in separate transactions.
    ///
    ///     Use <see cref="SystemClockTimerSystem" /> or <see cref="SecondsTimerSystem" /> unless you
    ///     need a clock of your own, in which case derive from
    ///     <see cref="TimerSystemImplementationBase{T}" /> and pass it here.
    /// </remarks>
    public class TimerSystem<T> : ITimerSystem<T>
        where T : IComparable<T>
    {
        private readonly ITimerSystemImplementation<T> implementation;

        private readonly Queue<Event> eventQueue = new Queue<Event>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="TimerSystem{T}" /> class over the given
        ///     implementation, starting it immediately.
        /// </summary>
        /// <param name="implementation">The clock and waiting mechanism to build on.</param>
        /// <param name="handleException">
        ///     Called with any exception raised while waiting for or firing timers.
        /// </param>
        /// <remarks>
        ///     Constructing a timer system starts its implementation and installs a transaction hook
        ///     which lives for the lifetime of the process, so these are meant to be created once rather
        ///     than per unit of work.
        /// </remarks>
        public TimerSystem(ITimerSystemImplementation<T> implementation, Action<Exception> handleException)
        {
            this.implementation = implementation;
            this.implementation.Start(handleException);
            BehaviorSink<T> timeSink = new BehaviorSink<T>(this.implementation.Now);
            this.Time = timeSink;
            Transaction.OnStart(
                () =>
                {
                    T t = this.implementation.Now;
                    this.implementation.RunTimersTo(t);
                    List<Event> events = new List<Event>();
                    while (true)
                    {
                        // Pop all events earlier than t.
                        lock (this.eventQueue)
                        {
                            if (this.eventQueue.Count > 0)
                            {
                                Event tempEvent = this.eventQueue.Peek();
                                if (tempEvent != null && tempEvent.Time.CompareTo(t) <= 0)
                                {
                                    events.Add(this.eventQueue.Dequeue());

                                    T timeToCheck = tempEvent.Time;
                                    while (this.eventQueue.Count > 0)
                                    {
                                        tempEvent = this.eventQueue.Peek();
                                        if (tempEvent != null && tempEvent.Time.CompareTo(timeToCheck) == 0)
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

                            Transaction.RunVoid(
                                () =>
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

        /// <summary>
        ///     Gets a behavior giving the current clock time.
        /// </summary>
        public Behavior<T> Time { get; }

        private class Event
        {
            internal Event(T time, StreamSink<T> alarm)
            {
                this.Time = time;
                this.Alarm = alarm;
            }

            internal readonly T Time;
            internal readonly StreamSink<T> Alarm;
        }

        /// <summary>
        ///     A timer that fires at the specified time.
        /// </summary>
        /// <param name="t">The time to fire at.</param>
        /// <returns>A stream which fires at the specified time.</returns>
        public Stream<T> At(Cell<Maybe<T>> t)
        {
            StreamSink<T> alarm = new StreamSink<T>();
            Maybe<ITimer> currentTimer = Maybe.None;
            IListener l = t.ListenStrong(
                m =>
                {
                    currentTimer.MatchSome(timer => timer.Cancel());
                    currentTimer = m.Match(
                        time => Maybe.Some(
                            this.implementation.SetTimer(
                                time,
                                () =>
                                {
                                    lock (this.eventQueue)
                                    {
                                        this.eventQueue.Enqueue(new Event(time, alarm));
                                    }
                                    // Open and close a transaction to trigger queued
                                    // events to run.
                                    Transaction.RunVoid(() => { });
                                })),
                        () => Maybe.None);
                });
            return alarm.AttachListener(l);
        }
    }
}
