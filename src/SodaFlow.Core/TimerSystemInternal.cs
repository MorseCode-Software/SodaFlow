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
    ///     holds. A change of the cell that commits cancels the timer that the
    ///     previous value set.
    /// </summary>
    /// <remarks>
    ///     The timer follows the cell through a transaction that fails. The cell
    ///     then keeps the deadline it had, and the alarm fires at that deadline.
    /// </remarks>
    /// <param name="deadline">The time of the alarm, or no time.</param>
    /// <param name="read">Gives the time in a value of the cell.</param>
    internal Stream<T> At<TDeadline>(Cell<TDeadline> deadline, Func<TDeadline, MaybeInternal<T>> read)
    {
        StreamSink<T> alarm = new();
        DeadlineTimer<TDeadline> timer = new(system: this, alarm: alarm, read: read);

        return alarm.AttachListenerInternal(deadline.ListenImpl(timer.Change));
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

    // The timer of one At. Only code in a transaction uses this object, thus the transaction lock
    // guards its fields.
    //
    // A change of the deadline sets the new timer in the transaction, and does not wait for the
    // commit. SetTimer throws on a stopped implementation. In the transaction, that throw fails
    // the transaction and the cell keeps its deadline. A throw after the commit comes when the
    // cell holds the new deadline, and that deadline then has no timer.
    //
    // The cancel of the previous timer waits for the commit. Before, the listener canceled it
    // immediately. When the transaction then did not commit, the cell kept its deadline with no
    // timer. The alarm fired at the deadline that did not commit.
    private sealed class DeadlineTimer<TDeadline>(
        in TimerSystemInternal<T, TTimer> system,
        in StreamSink<T> alarm,
        in Func<TDeadline, MaybeInternal<T>> read) : ILastAction
    {
        private readonly StreamSink<T> alarm = alarm;
        private readonly Func<TDeadline, MaybeInternal<T>> read = read;
        private readonly TimerSystemInternal<T, TTimer> system = system;

        // The timer of the deadline that the cell holds.
        private MaybeInternal<AlarmTimer> current;

        // True while the transaction that changed the deadline is open.
        private bool isChanged;

        // The timer of the deadline that the open transaction gave.
        private MaybeInternal<AlarmTimer> pending;

        internal void Change(TDeadline deadline)
        {
            if (this.isChanged)
            {
                // A second value in one transaction replaces the first.
                this.RevokePending();
            }
            else
            {
                this.isChanged = true;
                TransactionInternal.CurrentTransaction().Last(this);
            }

            if (this.read(deadline).TryGetValue(out T time))
            {
                this.pending =
                    MaybeInternal.Some(AlarmTimer.Create(system: this.system, time: time, alarm: this.alarm));
            }
        }

        void ILastAction.Commit()
        {
            // A cancel and not a revoke. The timer that the commit replaces was correct until this
            // transaction, thus an event that it queued before the cancel is a true alarm.
            if (this.current.TryGetValue(out AlarmTimer replaced))
            {
                replaced.Cancel();
            }

            this.current = this.pending;
            this.pending = MaybeInternal<AlarmTimer>.None;
            this.isChanged = false;
        }

        void ILastAction.Discard()
        {
            this.RevokePending();
            this.isChanged = false;
        }

        private void RevokePending()
        {
            if (!this.pending.TryGetValue(out AlarmTimer timer))
            {
                return;
            }

            this.pending = MaybeInternal<AlarmTimer>.None;
            timer.Revoke();
        }
    }

    // One timer that At set. A transaction that fails revokes the timer that it set, and a
    // revoked timer sends no alarm. A cancel is not sufficient for that. The implementation can
    // fire the timer on its own thread while the transaction is open. The event is then in the
    // queue before the transaction fails.
    private sealed class AlarmTimer
    {
        private readonly StreamSink<T> alarm;
        private readonly TimerSystemInternal<T, TTimer> system;
        private readonly T time;

        // The handle from the implementation. Only code in a transaction uses this.
        private MaybeInternal<TTimer> handle;

        // Code that holds the lock on eventQueue uses the two fields below.
        private bool isRevoked;

        private MaybeInternal<Event> queued;

        private AlarmTimer(TimerSystemInternal<T, TTimer> system, T time, StreamSink<T> alarm)
        {
            this.system = system;
            this.time = time;
            this.alarm = alarm;
        }

        // The implementation can run the callback before it gives the handle. The callback does
        // not use the handle, thus that is safe.
        internal static AlarmTimer Create(TimerSystemInternal<T, TTimer> system, T time, StreamSink<T> alarm)
        {
            AlarmTimer timer = new(system: system, time: time, alarm: alarm);
            timer.handle = MaybeInternal.Some(system.implementation.SetTimer(time: time, callback: timer.Arrive));

            return timer;
        }

        internal void Cancel()
        {
            if (this.handle.TryGetValue(out TTimer timer))
            {
                this.system.implementation.Cancel(timer);
            }
        }

        // The cancel is out of the lock, because it calls the code of the implementation.
        internal void Revoke()
        {
            lock (this.system.eventQueue)
            {
                this.isRevoked = true;

                if (this.queued.TryGetValue(out Event ev))
                {
                    this.system.eventQueue.Remove(ev);
                }
            }

            this.Cancel();
        }

        private void Arrive()
        {
            lock (this.system.eventQueue)
            {
                if (this.isRevoked)
                {
                    return;
                }

                Event ev = new(time: this.time, seq: this.system.nextEventSeq++, alarm: this.alarm);
                this.queued = MaybeInternal.Some(ev);
                this.system.eventQueue.Add(ev);
            }

            // A transaction that opens and closes runs the hook, which sends the events in the queue.
            TransactionInternal.RunVoidImpl(static () =>
            {
            });
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
