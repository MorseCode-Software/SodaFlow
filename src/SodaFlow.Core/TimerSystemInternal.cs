using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow;

/// <summary>
///     The timer system of each language package. It reads the clock at the start
///     of each transaction, sends the time, and sends the alarms that are due.
/// </summary>
/// <remarks>
///     <para>
///         Alarms get to the graph through a transaction hook that the constructor
///         installs. When a transaction starts, the hook reads the current time,
///         runs each timer that is due, and sends the alarms. It sends the alarms
///         at one time together, and the alarms at different times in different
///         transactions.
///     </para>
///     <para>
///         Each package has its own public timer system, with its own optional
///         type, and passes each member to this object. The implementation arrives
///         through <see cref="ITimerSystemImplementationInternal{T,TTimer}" />, and
///         <see cref="At{TDeadline}" /> takes a function that reads the optional
///         type. Before, each package had a copy of this code, and the two copies
///         drifted.
///     </para>
/// </remarks>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
/// <typeparam name="TTimer">The type of the handle that cancels a timer.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class TimerSystemInternal<T, TTimer> : IDisposable
{
    private readonly IComparer<T> comparer;

    // In the sequence of the time of each event, and of its arrival for one time. An implementation
    // runs a callback out of its lock, and it can run timers on more than one thread. Thus, the
    // events can get to this set in a different sequence from their times.
    private readonly SortedSet<Event> eventQueue;

    private readonly ITimerSystemImplementationInternal<T, TTimer> implementation;

    // The transaction hook. Dispose removes it, and the hook then runs in no transaction after that.
    private readonly IDisposable startHook;

    private readonly BehaviorSink<T> time;

    // The last value that the hook sent to Time. Only the hook uses this, and it runs with the
    // transaction lock.
    private T lastTime;

    // The arrival number of the next event. Only code that holds the lock on eventQueue uses this.
    private long nextEventSeq;

    /// <summary>
    ///     Starts <paramref name="implementation" />, and installs the transaction
    ///     hook.
    /// </summary>
    /// <param name="implementation">The clock and the wait.</param>
    /// <param name="comparer">Puts two times in order.</param>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    internal TimerSystemInternal(
        ITimerSystemImplementationInternal<T, TTimer> implementation,
        IComparer<T> comparer,
        Action<Exception> handleException)
    {
        this.implementation = implementation;
        this.comparer = comparer;
        this.eventQueue = new SortedSet<Event>(new EventComparer(comparer));
        this.implementation.Start(handleException);
        this.lastTime = this.implementation.Now;
        this.time = new BehaviorSink<T>(this.lastTime);
        this.startHook = TransactionInternal.OnStartImpl(this.OnStart);
    }

    /// <summary>Gets the behavior of the current time.</summary>
    internal Behavior<T> Time => this.time;

    /// <summary>
    ///     Removes the transaction hook, and stops the implementation. This drops an
    ///     event that is due and not sent. A second call does nothing.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage - This says what a disposal of a timer system stops.
    public void Dispose()
    {
        this.startHook.Dispose();
        this.implementation.Stop();
    }

    /// <summary>
    ///     Gives a stream that fires at the time that <paramref name="deadline" />
    ///     holds. A change of the cell cancels the timer that it set.
    /// </summary>
    /// <param name="deadline">The time of the alarm, or no time.</param>
    /// <param name="read">Gives the time in a value of the cell.</param>
    internal Stream<T> At<TDeadline>(Cell<TDeadline> deadline, Func<TDeadline, MaybeInternal<T>> read)
    {
        StreamSink<T> alarm = new();
        MaybeInternal<TTimer> currentTimer = MaybeInternal<TTimer>.None;

        IListener l =
            deadline.ListenImpl(d =>
            {
                if (currentTimer.TryGetValue(out TTimer timer))
                {
                    this.implementation.Cancel(timer);
                }

                currentTimer =
                    read(d).TryGetValue(out T t)
                        ? MaybeInternal.Some(
                            this.implementation.SetTimer(time: t, callback: () => this.Arrive(time: t, alarm: alarm)))
                        : MaybeInternal<TTimer>.None;
            });

        return alarm.AttachListenerInternal(l);
    }

    private void Arrive(T time, StreamSink<T> alarm)
    {
        lock (this.eventQueue)
        {
            this.eventQueue.Add(new Event(time: time, seq: this.nextEventSeq++, alarm: alarm));
        }

        // A transaction that opens and closes runs the hook, which sends the events in the queue.
        TransactionInternal.RunVoidImpl(static () =>
        {
        });
    }

    private void OnStart()
    {
        T t = this.implementation.Now;
        this.implementation.RunTimersTo(t);

        // This runs at the start of each transaction in the process, and most of them have no
        // event due. That path allocates nothing here: the list exists only when events are due.
        while (this.TakeDueEvents(t) is { } events)
        {
            T eventTime = events[0].Time;

            // An implementation removes a timer and then runs its callback out of its lock. A
            // transaction can start between the two and send a time after the time of the event.
            // The event then gets to the set after that time. Its alarm fires at its own time, and
            // Time does not go back. An event at the time that Time holds sends nothing.
            if (this.comparer.Compare(x: eventTime, y: this.lastTime) > 0)
            {
                this.time.SendImpl(eventTime);
                this.lastTime = eventTime;
            }

            TransactionInternal.RunVoidImpl(() =>
            {
                foreach (Event ev in events)
                {
                    ev.Alarm.SendImpl(ev.Time);
                }
            });
        }

        // The clock itself can go back, for example at the end of daylight saving time. Time
        // follows the clock then, as the clock is the authority for the current time. A clock that
        // did not move sends nothing: the updates of Time are its changes, as for each other
        // behavior. Before, this sent at each transaction, and each listener of the updates ran
        // with the value that Time held.
        if (this.comparer.Compare(x: t, y: this.lastTime) != 0)
        {
            this.time.SendImpl(t);
            this.lastTime = t;
        }
    }

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

            if (this.comparer.Compare(x: first.Time, y: t) > 0)
            {
                return null;
            }

            List<Event> events =
                [.. this.eventQueue.TakeWhile(ev => this.comparer.Compare(x: ev.Time, y: first.Time) == 0)];

            foreach (Event ev in events)
            {
                this.eventQueue.Remove(ev);
            }

            return events;
        }
    }

    private sealed class Event(in T time, in long seq, in StreamSink<T> alarm)
    {
        internal readonly StreamSink<T> Alarm = alarm;

        internal readonly long Seq = seq;

        internal readonly T Time = time;
    }

    // In the sequence of time, and in the sequence of arrival for one time.
    private sealed class EventComparer(in IComparer<T> comparer) : IComparer<Event>
    {
        private readonly IComparer<T> comparer = comparer;

        public int Compare(Event? x, Event? y)
        {
            if (ReferenceEquals(objA: x, objB: y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int timeComparison = this.comparer.Compare(x: x.Time, y: y.Time);
            return timeComparison != 0 ? timeComparison : x.Seq.CompareTo(value: y.Seq);
        }
    }
}
