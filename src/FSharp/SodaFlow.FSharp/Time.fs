/// <summary>
///     Timer systems: clocks, and streams which fire at times drawn from them.
/// </summary>
/// <remarks>
///     Use <c>SystemClockTimerSystem</c> or <c>SecondsTimerSystem</c> unless a clock of your own
///     is needed, in which case derive from <c>TimerSystemImplementationBase</c> and pass it to
///     <c>TimerSystem</c>.
///
///     Alarms reach the graph through a <c>Transaction.onStart</c> hook: when a transaction starts,
///     the hook reads the clock, runs any timer that has come due and sends the resulting alarms.
///     Alarms that came due at the same time are delivered together, and alarms at different times
///     in separate transactions.
/// </remarks>
module SodaFlow.Time

open System
open System.Linq
open System.Collections.Generic
open System.Threading

/// <summary>
///     A handle for canceling a timer that has been set.
/// </summary>
/// <remarks>
///     Disposing the timer does the same thing as canceling it; only one of the two is needed.
///     Apart from that, these do not need to be disposed.
/// </remarks>
type ITimer =
    inherit IDisposable
    /// <summary>
    ///     Cancels the timer, so that it will not fire.
    /// </summary>
    /// <remarks>
    ///     Has no effect if the timer has already fired or already been canceled, so it is safe to
    ///     call more than once.
    /// </remarks>
    abstract member Cancel : unit -> unit

/// <summary>
///     A source of time, and of streams which fire at times drawn from it.
/// </summary>
/// <typeparam name="'T">
///     The type used to express a point in time - <c>System.DateTime</c> for
///     <c>SystemClockTimerSystem</c>, <c>float</c> for <c>SecondsTimerSystem</c>.
/// </typeparam>
/// <remarks>
///     <c>TimerSystem</c> is the implementation that ships. Alarms from <c>At</c> arrive in a
///     transaction of their own, so nothing is required of the application beyond listening.
/// </remarks>
type ITimerSystem<'T when 'T : comparison> =
    /// <summary>
    ///     A behavior giving the current clock time.
    /// </summary>
    /// <remarks>
    ///     Updated as alarms are delivered rather than continuously, so it moves in the steps the
    ///     timers make it take, not with every tick of the underlying clock.
    /// </remarks>
    abstract member Time : Behavior<'T>
    /// <summary>
    ///     A stream which fires at the time held in a cell.
    /// </summary>
    /// <returns>
    ///     A stream firing the alarm time, once, each time the cell settles on a <c>Some</c> whose
    ///     time is reached.
    /// </returns>
    /// <remarks>
    ///     The cell holding <c>None</c> means no alarm is pending. Changing the cell cancels whatever
    ///     alarm was outstanding and schedules the new one, so this is how a timer is rescheduled or
    ///     called off. A time already in the past fires at the next opportunity rather than being
    ///     dropped.
    /// </remarks>
    abstract member At : Cell<'T option> -> Stream<'T>

/// <summary>
///     The clock and the waiting behind a <c>TimerSystem</c>.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Separated from <c>ITimerSystem</c> so that supplying a new clock does not mean
///     re-implementing the FRP. Derive from <c>TimerSystemImplementationBase</c> rather than
///     implementing this directly unless the scheduling itself needs replacing.
/// </remarks>
type ITimerSystemImplementation<'T> =
    /// <summary>
    ///     Starts whatever machinery this implementation uses to notice that a timer has come due.
    /// </summary>
    /// <remarks>
    ///     Called once, from the <c>TimerSystem</c> constructor. The function passed in is called with
    ///     any exception raised while waiting for or firing timers, and is expected to absorb it.
    ///
    ///     An implementation which waits should do so on a thread it owns rather than on the thread
    ///     pool, since alarms stop being delivered entirely if that wait cannot be scheduled.
    /// </remarks>
    abstract member Start : (exn -> unit) -> unit
    /// <summary>
    ///     Schedules a callback to run once the clock reaches the given time.
    /// </summary>
    /// <returns>A handle which can be used to cancel the timer before it fires.</returns>
    /// <remarks>
    ///     A time already in the past fires at the next opportunity rather than being dropped.
    /// </remarks>
    abstract member SetTimer : 'T -> (unit -> unit) -> ITimer
    /// <summary>
    ///     Fires every timer scheduled at or before the given time, on the calling thread.
    /// </summary>
    /// <remarks>
    ///     Called from the transaction start hook, which is what lets alarms be delivered by a
    ///     transaction that happens to start rather than only by the implementation itself.
    /// </remarks>
    abstract member RunTimersTo : 'T -> unit
    abstract member Now : 'T

type private Event<'T> = { Time : 'T; Alarm : StreamSink<'T> }

/// <summary>
///     A timer system built on an <c>ITimerSystemImplementation</c>, which supplies the clock and
///     the waiting; this type supplies the FRP.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <param name="implementation">The clock and waiting mechanism to build on.</param>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Constructing one starts its implementation and installs a <c>Transaction.onStart</c> hook
///     which lives for the lifetime of the process, so these are meant to be created once rather
///     than per unit of work.
///
///     Use <c>SystemClockTimerSystem</c> or <c>SecondsTimerSystem</c> unless a clock of your own
///     is needed.
/// </remarks>
type TimerSystem<'T when 'T : comparison> (implementation : 'T ITimerSystemImplementation, handleException : exn -> unit) =
    let eventQueue = Queue<Event<'T>>()
    let time = (fun () ->
        implementation.Start handleException
        let timeSink = BehaviorSink.create implementation.Now
        Transaction.onStart (fun () ->
            let t = implementation.Now
            implementation.RunTimersTo t
            let events = List<Event<'T>>()
            let rec processEvents () =
                lock eventQueue (fun () ->
                    if eventQueue.Count > 0 then
                        let event = eventQueue.Peek()
                        if event.Time <= t then
                            events.Add(eventQueue.Dequeue())
                            let timeToCheck = event.Time
                            let rec findMoreEvents() =
                                if eventQueue.Count > 0 then
                                    let event = eventQueue.Peek()
                                    if event.Time = timeToCheck then
                                        events.Add(eventQueue.Dequeue())
                                        findMoreEvents()
                            findMoreEvents())
                if events.Count > 0 then
                    timeSink |> BehaviorSink.send events.[0].Time
                    Transaction.run(fun () ->
                        events |> Seq.iter (fun event ->
                            event.Alarm |> StreamSink.send event.Time))
                    events.Clear()
                    processEvents()
            processEvents ()
            timeSink |> BehaviorSink.send t)
        timeSink :> 'T Behavior) ()

    interface 'T ITimerSystem with
        member __.Time = time
        member __.At t =
            let alarm = StreamSink.create ()
            let mutable currentTimer : ITimer option = None
            let listener = t |> Cell.listenStrong (fun o ->
                currentTimer |> Option.iter (fun timer -> timer.Cancel ())
                currentTimer <-
                    o |>
                        Option.map (fun time ->
                            implementation.SetTimer time (fun () ->
                                lock eventQueue (fun () -> eventQueue.Enqueue { Time = time; Alarm = alarm })
                                Transaction.run id)))
            alarm |> Stream.attachListener listener

type private WaitOrFire =
    | Wait of TimeSpan
    | Fire of (unit -> unit)

/// <summary>
///     A base for timer system implementations which supplies the scheduling, leaving a derived type
///     to supply only the clock.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Override <c>Now</c> and <c>SubtractTimes</c>; the ordering, waiting and firing of timers is
///     handled here.
///
///     The waiting is done on a dedicated background thread rather than on the thread pool.
///     Nothing else fires alarms on its own - the transaction start hook only runs timers when some
///     transaction happens to start - so an application that is merely waiting for an alarm depends
///     entirely on that loop, and a loop that can be starved by pool work would stop delivering
///     alarms altogether. The thread does not keep the process alive.
/// </remarks>
[<AbstractClass>]
type TimerSystemImplementationBase<'T when 'T : comparison>() as this =
    let lockObject = obj ()
    let timers = SortedSet<SimpleTimer<'T>> ()

    // Signaled whenever the timer set changes, to wake the timer thread so it can recompute how
    // long to wait. An AutoResetEvent rather than a CancellationTokenSource: a signal raised while
    // the thread is between computing its wait and entering it is latched, so the next wait returns
    // immediately instead of sleeping through the change. The previous design allocated a fresh
    // CancellationTokenSource on every iteration and never disposed one.
    let timersChanged = new AutoResetEvent (false)

    let mutable nextSeq = 0

    let rec timeUntilNext now =
        let waitOrFire = lock lockObject (fun () ->
            if timers.Count < 1 then Wait (TimeSpan.FromSeconds (1000.0))
            else
                let timer = timers.First ()
                let waitTime = this.SubtractTimes timer.Time now
                if waitTime <= TimeSpan.Zero then
                    timers.Remove(timer) |> ignore
                    Fire timer.Callback
                else Wait waitTime)
        match waitOrFire with
            | Wait waitTime -> waitTime
            | Fire callback ->
                callback()
                timeUntilNext now

    member internal __.LockObject = lockObject
    member internal __.Timers = timers
    member val internal NextSeq = nextSeq with get, set

    /// <summary>
    ///     Returns how much time separates two points on this implementation's clock.
    /// </summary>
    /// <returns>
    ///     The interval from the second argument to the first, negative if the first is the earlier of
    ///     the two.
    /// </returns>
    /// <remarks>
    ///     Used to work out how long to wait for the next timer, so it must return a real duration
    ///     rather than a comparison result.
    /// </remarks>
    abstract member SubtractTimes : 'T -> 'T -> TimeSpan

    /// <summary>
    ///     The current time according to this implementation's clock.
    /// </summary>
    /// <remarks>
    ///     Read on every pass of the waiting loop, so it should be cheap, and it must move forward
    ///     monotonically enough that scheduled times are eventually reached.
    /// </remarks>
    abstract member Now : 'T

    interface 'T ITimerSystemImplementation with
        // A dedicated thread rather than the thread pool.
        //
        // Nothing else fires alarms: the Transaction.onStart hook calls RunTimersTo, but only when
        // some transaction happens to start, so an application that is merely waiting depends
        // entirely on this loop. Running it with Async.Start made that dependency a liveness
        // hazard - every iteration needed a pool thread, once to start and again for each
        // Task.Delay continuation, and a cancellation only queued that continuation. With the pool
        // saturated the loop simply never ran, and alarms were never fired at all.
        //
        // A background thread cannot be starved by pool work, and WaitOne serves as both the timed
        // wait and the wake. This mirrors the C# implementation, where the same bug was diagnosed
        // by saturating the pool: eight of eight runs delivered zero events after waiting two
        // seconds for alarms a hundred milliseconds out, against zero of eight unstarved.
        member this.Start handleException =
            let timerThread =
                Thread (
                    ThreadStart (fun () ->
                        while true do
                            try
                                let waitTime = timeUntilNext this.Now
                                if waitTime > TimeSpan.Zero then
                                    timersChanged.WaitOne waitTime |> ignore
                            with
                                | e -> handleException e),
                    Name = "SodaFlow Timer Thread",
                    IsBackground = true)

            timerThread.Start ()

        member this.SetTimer time callback =
            let timer = new SimpleTimer<_> (this, time, callback)
            lock lockObject (fun () -> timers.Add(timer) |> ignore)

            // Signaled outside the lock. Canceling the old token source was done while holding
            // it, and cancellation runs its callbacks synchronously, so the waiting loop could
            // resume inline on this thread and re-enter timeUntilNext while the caller still held
            // the lock.
            timersChanged.Set () |> ignore
            upcast timer

        member __.RunTimersTo now = timeUntilNext now |> ignore

        member this.Now = this.Now

/// <summary>
///     One scheduled timer within a <c>TimerSystemImplementationBase</c>.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <param name="implementation">The implementation this timer is scheduled in.</param>
/// <param name="time">The time at which the callback is to run.</param>
/// <param name="callback">The callback to run.</param>
/// <remarks>
///     Returned from <c>SetTimer</c> as an <c>ITimer</c>; there is no reason to construct one
///     directly. Ordered by time, and by creation order where two share a time, so that timers set
///     for the same instant fire in the order they were set.
/// </remarks>
and SimpleTimer<'T when 'T : comparison> (implementation : 'T TimerSystemImplementationBase, time : 'T, callback : unit -> unit) as this =
    let seq = lock implementation.LockObject (fun () ->
        let seq = implementation.NextSeq
        implementation.NextSeq <- implementation.NextSeq + 1
        seq)

    let compareEntries (x : 'T SimpleTimer) (y : 'T SimpleTimer) =
        let timeComparison = compare x.Time y.Time
        if timeComparison <> 0 then timeComparison
        else compare x.Seq y.Seq

    // Deliberately does not signal the timer thread. Waking it early to recompute a deadline that
    // has only got later gains nothing, and with an AutoResetEvent it costs: the signal here
    // releases the waiter, and the Set in whichever SetTimer replaces this timer latches for the
    // next wait, so one replacement drives two recompute cycles where a stale wait replaced once
    // would have done.
    let cancel () = lock implementation.LockObject (fun () -> implementation.Timers.Remove(this) |> ignore)

    member internal __.Seq = seq
    member internal __.Time = time
    member internal __.Callback = callback

    interface ITimer with
        member this.Cancel () = cancel ()

    /// <summary>
    ///     Determines whether another object is the same scheduled timer.
    /// </summary>
    /// <returns>
    ///     <c>true</c> if the other object is a timer of the same type scheduled for the same time and
    ///     with the same creation order.
    /// </returns>
    override this.Equals(otherObj) =
        match otherObj with
        | :? SimpleTimer<'T> as other -> this.Time = other.Time && this.Seq = other.Seq
        | _ -> false

    /// <summary>
    ///     Returns a hash code for this timer.
    /// </summary>
    /// <returns>A hash of the time the timer is scheduled for.</returns>
    override this.GetHashCode() = hash this.Time

    interface IComparable<'T SimpleTimer> with
        member this.CompareTo other = compareEntries this other

    interface IComparable with
        member this.CompareTo otherObj =
            match otherObj with
            | :? SimpleTimer<'T> as other -> compareEntries this other
            | _ -> invalidArg "other" "Cannot compare values of different types."

    interface IDisposable with
        member this.Dispose () = cancel ()

type private SystemClockTimerSystemImplementation() =
    inherit TimerSystemImplementationBase<DateTime>()
    override __.SubtractTimes first second = first - second
    override __.Now = DateTime.Now

/// <summary>
///     A timer system measuring time with the system clock.
/// </summary>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Times are <c>System.DateTime</c> values, so alarms can be set against wall-clock times
///     directly. Note that the clock can move backwards - a manual change, or a daylight saving
///     adjustment - and an alarm set past such a jump waits until the clock reaches it again.
/// </remarks>
type SystemClockTimerSystem(handleException : exn -> unit) =
    inherit TimerSystem<DateTime>(SystemClockTimerSystemImplementation(), handleException)

type private SecondsTimerSystemImplementation() =
    inherit TimerSystemImplementationBase<float>()
    let startTime = DateTime.Now
    override __.SubtractTimes first second = TimeSpan.FromSeconds(first - second)
    override __.Now = (DateTime.Now - startTime).TotalSeconds

/// <summary>
///     A timer system measuring time as the number of seconds elapsed since it was created.
/// </summary>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Times are <c>float</c> seconds. Convenient where alarms are naturally expressed as delays
///     rather than as points in time.
/// </remarks>
type SecondsTimerSystem(handleException : exn -> unit) =
    inherit TimerSystem<float>(SecondsTimerSystemImplementation(), handleException)
