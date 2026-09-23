/// <summary>
///     Timer systems: clocks, and streams which fire at times drawn from them.
/// </summary>
/// <remarks>
///     Use <c>SystemClockTimerSystem</c> or <c>SecondsTimerSystem</c>. For a different clock,
///     derive a type from <c>TimerSystemImplementationBase</c> and give it to <c>TimerSystem</c>.
///
///     Alarms get to the graph through a <c>Transaction.onStart</c> handler. When a transaction starts,
///     the handler reads the clock, runs each timer that is due, and sends the alarms. It sends the
///     alarms that became due at the same time together, and it sends alarms at different times in
///     different transactions.
/// </remarks>
module SodaFlow.Time

open System
open System.Linq
open System.Collections.Generic
open System.Threading

/// <summary>
///     A handle to cancel a timer.
/// </summary>
/// <remarks>
///     A disposal of the timer has the same result as a cancellation. Only one of the two is
///     necessary. In each other condition, a disposal of a timer is not necessary.
/// </remarks>
type ITimer =
    inherit IDisposable
    /// <summary>
    ///     Cancels the timer, so that it will not fire.
    /// </summary>
    /// <remarks>
    ///     This does nothing when the timer fired, and when a call canceled it, so it is safe to
    ///     call more than one time.
    /// </remarks>
    abstract member Cancel: unit -> unit

/// <summary>
///     A source of time, and of streams which fire at times drawn from it.
/// </summary>
/// <typeparam name="'T">
///     The type used to express a point in time - <c>System.DateTime</c> for
///     <c>SystemClockTimerSystem</c>, <c>float</c> for <c>SecondsTimerSystem</c>.
/// </typeparam>
/// <remarks>
///     <c>TimerSystem</c> is the implementation in this library. Alarms from <c>At</c> get to the
///     graph in a transaction of their own, thus other code must only listen.
/// </remarks>
type ITimerSystem<'T when 'T: comparison> =
    /// <summary>
    ///     A behavior giving the current clock time.
    /// </summary>
    /// <remarks>
    ///     This changes as the timer system sends alarms, and not continuously. Thus, it moves in the
    ///     steps of the timers, and not with each count of the clock below it.
    /// </remarks>
    abstract member Time: Behavior<'T>
    /// <summary>
    ///     A stream which fires at the time held in a cell.
    /// </summary>
    /// <returns>
    ///     A stream that fires the alarm time one time. This occurs at each move of the cell to a
    ///     <c>Some</c> with a time that the clock gets to.
    /// </returns>
    /// <remarks>
    ///     A cell that holds <c>None</c> means that no alarm is pending. A change to the cell cancels
    ///     the alarm that was set and schedules the new alarm. Thus, this is how code sets a timer
    ///     again, and how it cancels a timer. A time before now fires at the next opportunity, and the
    ///     timer system does not drop it.
    /// </remarks>
    abstract member At: Cell<'T option> -> Stream<'T>

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
    ///     The <c>TimerSystem</c> constructor calls this one time. This code calls the given function
    ///     with each exception from a wait for a timer, and from a timer that fires. That function must
    ///     absorb the exception.
    ///
    ///     An implementation that waits must wait on a thread of its own, and not on the thread pool.
    ///     The alarms stop fully when the pool cannot schedule that wait.
    /// </remarks>
    abstract member Start: (exn -> unit) -> unit
    /// <summary>
    ///     Schedules a callback to run when the clock reaches the given time.
    /// </summary>
    /// <returns>A handle to cancel the timer before it fires.</returns>
    /// <remarks>
    ///     A time before now fires at the next opportunity, and the timer system does not drop it.
    /// </remarks>
    abstract member SetTimer: 'T -> (unit -> unit) -> ITimer
    /// <summary>
    ///     Fires each timer scheduled at or before the given time, on the calling thread.
    /// </summary>
    /// <remarks>
    ///     The <c>Transaction.onStart</c> handler calls this. Thus, a transaction that starts can send alarms, and
    ///     not only the implementation.
    /// </remarks>
    abstract member RunTimersTo: 'T -> unit
    abstract member Now: 'T

type private Event<'T> = { Time: 'T; Alarm: StreamSink<'T> }

/// <summary>
///     A timer system on an <c>ITimerSystemImplementation</c>, which gives the clock and the wait
///     mechanism. This type gives the FRP.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <param name="implementation">The clock and waiting mechanism to build on.</param>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     The construction of one starts its implementation and installs a <c>Transaction.onStart</c>
///     handler that stays for the life of the process. Thus, make one of these one time, and not one
///     for each unit of work.
///
///     Use <c>SystemClockTimerSystem</c> or <c>SecondsTimerSystem</c>, unless a different clock is
///     necessary.
/// </remarks>
type TimerSystem<'T when 'T: comparison>(implementation: 'T ITimerSystemImplementation, handleException: exn -> unit) =
    let eventQueue = Queue<Event<'T>>()

    let time =
        (fun () ->
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

                                let rec findMoreEvents () =
                                    if eventQueue.Count > 0 then
                                        let event = eventQueue.Peek()

                                        if event.Time = timeToCheck then
                                            events.Add(eventQueue.Dequeue())
                                            findMoreEvents ()

                                findMoreEvents ())

                    if events.Count > 0 then
                        timeSink |> BehaviorSink.send events[0].Time

                        Transaction.run (fun () ->
                            events |> Seq.iter (fun event -> event.Alarm |> StreamSink.send event.Time))

                        events.Clear()
                        processEvents ()

                processEvents ()
                timeSink |> BehaviorSink.send t)

            timeSink :> 'T Behavior) ()

    interface 'T ITimerSystem with
        member _.Time = time

        member _.At t =
            let alarm = StreamSink.create ()
            let mutable currentTimer: ITimer option = None

            let listener =
                t
                |> Cell.listenStrong (fun o ->
                    currentTimer |> Option.iter (fun timer -> timer.Cancel())

                    currentTimer <-
                        o
                        |> Option.map (fun time ->
                            implementation.SetTimer time (fun () ->
                                lock eventQueue (fun () -> eventQueue.Enqueue { Time = time; Alarm = alarm })
                                Transaction.run id)))

            alarm.AttachListenerInternal listener

type private WaitOrFire =
    | Wait of TimeSpan
    | Fire of (unit -> unit)

/// <summary>
///     A base for timer system implementations which supplies the scheduling, leaving a derived type
///     to supply only the clock.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Override <c>Now</c> and <c>SubtractTimes</c>. This type puts the timers in order, waits, and
///     fires them.
///
///     The wait occurs on a dedicated background thread, and not on the thread pool. No other code fires
///     alarms, because the <c>Transaction.onStart</c> handler runs timers only when a transaction starts. Thus,
///     code that waits for an alarm is dependent on that loop, and pool work that starves the loop stops the
///     alarms. The thread does not keep the process in operation.
/// </remarks>
[<AbstractClass>]
type TimerSystemImplementationBase<'T when 'T: comparison>() as this =
    let lockObject = obj ()
    let timers = SortedSet<SimpleTimer<'T>>()

    // A change to the timer set signals this, to wake the timer thread. The thread then calculates
    // the new wait. This is an AutoResetEvent and not a CancellationTokenSource. A signal latches
    // after the thread calculates its wait and before that wait starts. Thus, the next wait
    // returns immediately, and does not sleep through the change. The initial code allocated a new
    // CancellationTokenSource at each step, and disposed of none of them.
    let timersChanged = new AutoResetEvent(false)

    let mutable nextSeq = 0

    let rec timeUntilNext now =
        let waitOrFire =
            lock lockObject (fun () ->
                if timers.Count < 1 then
                    Wait(TimeSpan.FromSeconds(1000.0))
                else
                    let timer = timers.First()
                    let waitTime = this.SubtractTimes timer.Time now

                    if waitTime <= TimeSpan.Zero then
                        timers.Remove(timer) |> ignore
                        Fire timer.Callback
                    else
                        Wait waitTime)

        match waitOrFire with
        | Wait waitTime -> waitTime
        | Fire callback ->
            callback ()
            timeUntilNext now

    member internal _.LockObject = lockObject
    member internal _.Timers = timers
    member val internal NextSeq = nextSeq with get, set

    /// <summary>
    ///     Returns how much time separates two points on this implementation's clock.
    /// </summary>
    /// <returns>
    ///     The interval from the second argument to the first, negative if the first is the earlier of
    ///     the two.
    /// </returns>
    /// <remarks>
    ///     This calculates the wait for the next timer, thus it must return a true interval, and not
    ///     the result of a compare.
    /// </remarks>
    abstract member SubtractTimes: 'T -> 'T -> TimeSpan

    /// <summary>
    ///     The current time according to this implementation's clock.
    /// </summary>
    /// <remarks>
    ///     The wait loop reads this at each step, thus its cost must be low. It must also move forward
    ///     sufficiently for the clock to get to each scheduled time.
    /// </remarks>
    abstract member Now: 'T

    interface 'T ITimerSystemImplementation with
        // A dedicated thread rather than the thread pool.
        //
        // No other code fires alarms. The <c>Transaction.onStart</c> handler calls RunTimersTo, but
        // only when a transaction starts. Thus, code that only waits is dependent on this loop.
        // Async.Start made that dependency dangerous for liveness. Each step needed a pool thread: one
        // time to start, and again for each Task.Delay continuation. A cancellation only put that
        // continuation in the queue. With a full pool the loop never ran, and no alarm fired.
        //
        // Pool work cannot starve a background thread, and WaitOne is the timed wait and the wake. This
        // is the same as the C# implementation, where a full pool showed the same bug. Eight of eight
        // runs sent zero events after a wait of two seconds. The alarms were one hundred milliseconds in
        // the future. Zero of eight runs gave this result with a pool that was not full.
        member this.Start handleException =
            let timerThread =
                Thread(
                    ThreadStart(fun () ->
                        while true do
                            try
                                let waitTime = timeUntilNext this.Now

                                if waitTime > TimeSpan.Zero then
                                    timersChanged.WaitOne waitTime |> ignore
                            with e ->
                                handleException e),
                    Name = "SodaFlow Timer Thread",
                    IsBackground = true
                )

            timerThread.Start()

        member this.SetTimer time callback =
            let timer = new SimpleTimer<_>(this, time, callback)
            lock lockObject (fun () -> timers.Add(timer) |> ignore)

            // This signals out of the lock. The initial code canceled the previous token source with the
            // lock held, and a cancellation runs its callbacks synchronously. Thus, the wait loop can
            // start again inline on this thread, and enter timeUntilNext again while the caller holds the
            // lock.
            timersChanged.Set() |> ignore
            upcast timer

        member _.RunTimersTo now = timeUntilNext now |> ignore

        member this.Now = this.Now

/// <summary>
///     One scheduled timer in a <c>TimerSystemImplementationBase</c>.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <param name="implementation">The implementation that holds this timer.</param>
/// <param name="time">The time at which the callback is to run.</param>
/// <param name="callback">The callback to run.</param>
/// <remarks>
///     <c>SetTimer</c> gives one of these as an <c>ITimer</c>. Do not make one directly. These are
///     in order of time, and in order of construction where two have the same time. Thus, timers
///     for the same instant fire in the sequence that the code set them.
/// </remarks>
and SimpleTimer<'T when 'T: comparison>
    (implementation: 'T TimerSystemImplementationBase, time: 'T, callback: unit -> unit) as this =
    let seq =
        lock implementation.LockObject (fun () ->
            let seq = implementation.NextSeq
            implementation.NextSeq <- implementation.NextSeq + 1
            seq)

    let compareEntries (x: 'T SimpleTimer) (y: 'T SimpleTimer) =
        let timeComparison = compare x.Time y.Time

        if timeComparison <> 0 then
            timeComparison
        else
            compare x.Seq y.Seq

    // This does not signal the timer thread, on purpose. A signal to calculate a deadline that
    // moved forward gains nothing, and with an AutoResetEvent it has a cost. The signal here
    // releases the waiter, and the Set in the SetTimer that replaces this timer latches for the
    // next wait. Thus, one replacement causes the thread to calculate two times, and one time is
    // sufficient.
    let cancel () =
        lock implementation.LockObject (fun () -> implementation.Timers.Remove(this) |> ignore)

    member internal _.Seq = seq
    member internal _.Time = time
    member internal _.Callback = callback

    interface ITimer with
        member this.Cancel() = cancel ()

    /// <summary>
    ///     Gives true when a second object is the same scheduled timer.
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
    /// <returns>A hash of the time of the timer.</returns>
    override this.GetHashCode() = hash this.Time

    interface IComparable<'T SimpleTimer> with
        member this.CompareTo other = compareEntries this other

    interface IComparable with
        member this.CompareTo otherObj =
            match otherObj with
            | :? SimpleTimer<'T> as other -> compareEntries this other
            | _ -> invalidArg "other" "Cannot compare values of different types."

    interface IDisposable with
        member this.Dispose() = cancel ()

type private SystemClockTimerSystemImplementation() =
    inherit TimerSystemImplementationBase<DateTime>()
    override _.SubtractTimes first second = first - second
    override _.Now = DateTime.Now

/// <summary>
///     A timer system measuring time with the system clock.
/// </summary>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Times are <c>System.DateTime</c> values, thus code can set an alarm against a wall-clock
///     time directly. The clock can move to an earlier time, after a manual change or a daylight
///     saving adjustment. An alarm after such a change waits until the clock gets to it again.
/// </remarks>
type SystemClockTimerSystem(handleException: exn -> unit) =
    inherit TimerSystem<DateTime>(SystemClockTimerSystemImplementation(), handleException)

type private SecondsTimerSystemImplementation() =
    inherit TimerSystemImplementationBase<float>()
    let startTime = DateTime.Now
    override _.SubtractTimes first second = TimeSpan.FromSeconds(first - second)
    override _.Now = (DateTime.Now - startTime).TotalSeconds

/// <summary>
///     A timer system that measures time as the number of seconds after its construction.
/// </summary>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Times are <c>float</c> seconds. Convenient where alarms are naturally expressed as delays
///     rather than as points in time.
/// </remarks>
type SecondsTimerSystem(handleException: exn -> unit) =
    inherit TimerSystem<float>(SecondsTimerSystemImplementation(), handleException)
