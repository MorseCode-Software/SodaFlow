/// <summary>
///     Timer systems: clocks, and streams which fire at times drawn from them.
/// </summary>
/// <remarks>
///     Use <c>SystemClockTimerSystem</c>, <c>UtcClockTimerSystem</c>, or <c>SecondsTimerSystem</c>.
///     For a different clock, derive a type from <c>TimerSystemImplementationBase</c> and give it
///     to <c>TimerSystem</c>.
///
///     Alarms get to the graph through a <c>Transaction.onStart</c> handler. When a transaction starts,
///     the handler reads the clock, runs each timer that is due, and sends the alarms. It sends the
///     alarms that became due at the same time together, and it sends alarms at different times in
///     different transactions.
/// </remarks>
module SodaFlow.Time

open System
open System.Runtime.CompilerServices

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
///
///     A timer system holds a transaction hook, and an implementation that waits holds a thread. A
///     disposal releases the two. After it, <c>Time</c> does not move, and no alarm fires.
/// </remarks>
type ITimerSystem<'T when 'T: comparison> =
    inherit IDisposable
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
    ///     Stops the machinery that <c>Start</c> began.
    /// </summary>
    /// <remarks>
    ///     <c>TimerSystem</c> calls this from its disposal. After it, no timer fires: not on the
    ///     implementation's own thread, and not from <c>RunTimersTo</c>. A timer that <c>SetTimer</c>
    ///     sets after it never fires. A second call does nothing. An implementation that waits on a
    ///     thread of its own ends that thread here. It does not wait for that thread when the call
    ///     comes from the thread itself.
    /// </remarks>
    abstract member Stop: unit -> unit
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

// Passes each member that the Core timer system uses to the public implementation.
type private ImplementationAdapter<'T>(implementation: 'T ITimerSystemImplementation) =
    interface ITimerSystemImplementationInternal<'T, ITimer> with
        member _.Now = implementation.Now
        member _.Start(handleException: Action<exn>) = implementation.Start handleException.Invoke
        member _.Stop() = implementation.Stop()
        member _.SetTimer(time, callback: Action) = implementation.SetTimer time callback.Invoke
        member _.Cancel(timer: ITimer) = timer.Cancel()
        member _.RunTimersTo time = implementation.RunTimersTo time

/// <summary>
///     A timer system on an <c>ITimerSystemImplementation</c>, which gives the clock and the wait
///     mechanism. This type gives the FRP.
/// </summary>
/// <typeparam name="'T">The type used to express a point in time.</typeparam>
/// <param name="implementation">The clock and waiting mechanism to build on.</param>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     The construction of one starts its implementation and installs a <c>Transaction.onStart</c>
///     handler. The handler runs at the start of each transaction in the process until a disposal
///     removes it. Thus, make one timer system for the process, or dispose each one when its work is
///     complete. A disposal also stops the implementation. After it, <c>Time</c> does not move, and no
///     alarm fires. The disposal drops an event that is due and not sent. A second disposal does
///     nothing, and a disposal from the listener of an alarm is safe.
///
///     Use <c>SystemClockTimerSystem</c>, <c>UtcClockTimerSystem</c>, or <c>SecondsTimerSystem</c>,
///     unless a different clock is necessary.
/// </remarks>
type TimerSystem<'T when 'T: comparison>(implementation: 'T ITimerSystemImplementation, handleException: exn -> unit) =
    // The timer system itself is in Core, and the C# package uses the same one. This type gives its
    // public surface, with option as the optional type.
    let timerSystem =
        new TimerSystemInternal<'T, ITimer>(
            ImplementationAdapter implementation,
            LanguagePrimitives.FastGenericComparer<'T>,
            Action<exn> handleException
        )

    interface IDisposable with
        member _.Dispose() = timerSystem.Dispose()

    interface 'T ITimerSystem with
        member _.Time = timerSystem.Time

        member _.At t =
            timerSystem.At(
                t,
                Func<'T option, MaybeInternal<'T>>(function
                    | Some time -> MaybeInternal.Some time
                    | None -> MaybeInternal<'T>.None)
            )

// The timer of the Core scheduler, which is also the handle that SetTimer gives. Thus, a timer is
// one object.
type private SimpleTimer<'T>(scheduler: TimerScheduler<'T>, time: 'T, callback: Action) =
    inherit ScheduledTimer<'T>(scheduler, time, callback)

    interface ITimer with
        member this.Cancel() = this.Cancel()

    interface IDisposable with
        member this.Dispose() = this.Cancel()

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
    // The scheduling is in Core, and the C# package uses the same one. This type gives it the clock
    // of the derived type.
    let scheduler =
        TimerScheduler<'T>(
            LanguagePrimitives.FastGenericComparer<'T>,
            Func<'T>(fun () -> this.Now),
            Func<'T, 'T, TimeSpan>(fun first second -> this.SubtractTimes first second)
        )

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
        member _.Start handleException = scheduler.Start(Action<exn> handleException)

        member _.Stop() = scheduler.Stop()

        member this.SetTimer time callback =
            let timer = new SimpleTimer<'T>(scheduler, time, Action callback)

            // A timer set after Stop never fires, and a handle for it tells the caller nothing.
            if not (scheduler.TryAdd timer) then
                raise (ObjectDisposedException(this.GetType().FullName))

            upcast timer

        member _.RunTimersTo now = scheduler.RunTimersTo now

        member this.Now = this.Now

// The clock is in Core, and the C# package uses the same one.
type private SystemClockTimerSystemImplementation(now: unit -> DateTime, resolution: TimeSpan) =
    inherit TimerSystemImplementationBase<DateTime>()

    let clock = SystemClock(Func<DateTime> now, resolution)

    override _.SubtractTimes first second = SystemClock.Subtract(first, second)

    override _.Now = clock.Now

/// <summary>
///     A timer system that measures time as a <c>System.DateTime</c> from a clock. The default
///     clock is <c>DateTime.Now</c>, the local time.
/// </summary>
/// <param name="now">Gives the current time. The timer thread reads it frequently.</param>
/// <param name="resolution">
///     The step that each value of the clock rounds down to, or <c>TimeSpan.Zero</c> for the
///     value as it is. The constructor refuses a negative step.
/// </param>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Times are <c>System.DateTime</c> values, thus code can set an alarm against a wall-clock
///     time directly. Local time moves with the clock of the machine, after a manual change or a
///     daylight saving adjustment. An alarm across such a change waits until the moved clock gets
///     to it. Use <c>UtcClockTimerSystem</c> for a wait or a deadline, and this type for a time
///     that a screen shows. A test gives a clock that it moves by hand.
///     <c>Time</c> updates only when the value of the clock differs from the last one. Thus, with
///     a resolution, it holds its value through the transactions in one step, and each of those
///     transactions skips the update. An alarm fires in the first transaction whose clock value is
///     at or after its time. Thus, an alarm can fire up to one step late, with its own time as
///     before.
/// </remarks>
type SystemClockTimerSystem(now: unit -> DateTime, resolution: TimeSpan, handleException: exn -> unit) =
    inherit TimerSystem<DateTime>(SystemClockTimerSystemImplementation(now, resolution), handleException)

    /// <summary>A timer system over <paramref name="now" />, with the value as it is.</summary>
    /// <param name="now">Gives the current time. The timer thread reads it frequently.</param>
    /// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
    new(now: unit -> DateTime, handleException: exn -> unit) =
        new SystemClockTimerSystem(now, TimeSpan.Zero, handleException)

    /// <summary>
    ///     A timer system over <c>DateTime.Now</c> that gives its value in steps of
    ///     <paramref name="resolution" />.
    /// </summary>
    /// <param name="resolution">
    ///     The step that each value of the clock rounds down to, or <c>TimeSpan.Zero</c> for the
    ///     value as it is. The constructor refuses a negative step.
    /// </param>
    /// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
    new(resolution: TimeSpan, handleException: exn -> unit) =
        new SystemClockTimerSystem((fun () -> DateTime.Now), resolution, handleException)

    /// <summary>A timer system over <c>DateTime.Now</c>.</summary>
    /// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
    new(handleException: exn -> unit) =
        new SystemClockTimerSystem((fun () -> DateTime.Now), TimeSpan.Zero, handleException)

/// <summary>
///     A timer system that measures time with <c>DateTime.UtcNow</c>.
/// </summary>
/// <param name="resolution">
///     The step that each value of the clock rounds down to, or <c>TimeSpan.Zero</c> for the
///     value as it is. The constructor refuses a negative step.
/// </param>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     UTC does not move at a change of daylight saving time, thus a timer set five minutes ahead
///     waits five minutes at each date. Use this type for a wait or a deadline, and
///     <c>SystemClockTimerSystem</c> for a time that a screen shows. See that type for what a
///     resolution does.
/// </remarks>
type UtcClockTimerSystem(resolution: TimeSpan, handleException: exn -> unit) =
    inherit SystemClockTimerSystem((fun () -> DateTime.UtcNow), resolution, handleException)

    /// <summary>A timer system over <c>DateTime.UtcNow</c>, with the value as it is.</summary>
    /// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
    new(handleException: exn -> unit) = new UtcClockTimerSystem(TimeSpan.Zero, handleException)

// The clock is in Core, and the C# package uses the same one.
type private SecondsTimerSystemImplementation(resolution: TimeSpan) =
    inherit TimerSystemImplementationBase<float>()

    let clock = SecondsClock(resolution)

    override _.SubtractTimes first second = SecondsClock.Subtract(first, second)

    override _.Now = clock.Now

/// <summary>
///     A timer system that measures time as the number of seconds after its construction.
/// </summary>
/// <param name="resolution">
///     The step that each value of the clock rounds down to, or <c>TimeSpan.Zero</c> for the
///     value as it is. The constructor refuses a negative step.
/// </param>
/// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
/// <remarks>
///     Times are <c>float</c> seconds. Convenient where alarms are naturally expressed as delays
///     rather than as points in time. See <c>SystemClockTimerSystem</c> for what a resolution does.
/// </remarks>
type SecondsTimerSystem(resolution: TimeSpan, handleException: exn -> unit) =
    inherit TimerSystem<float>(SecondsTimerSystemImplementation(resolution), handleException)

    /// <summary>A timer system in seconds, with the value as it is.</summary>
    /// <param name="handleException">Called with any exception raised while waiting for or firing timers.</param>
    new(handleException: exn -> unit) = new SecondsTimerSystem(TimeSpan.Zero, handleException)

/// <summary>
///     Fires the last value of a stream after a time with no other value. Each firing moves the
///     alarm out, thus a sequence of firings with no space between them gives one value.
/// </summary>
/// <remarks>
///     Each value that a quiet time follows fires one time, and the values before it fire never. A
///     search box is the usual position for this: the text of each keystroke goes in, and one search
///     comes out.
///
///     The result is in a transaction of the alarm and never in the transaction of a firing. Thus, a
///     graph that reads the two sees two instants, which is correct: the values are at two times.
///
///     This does not cancel work that started. Where each value starts an operation, give a strategy
///     to <c>mapAsync</c> that cancels the operation it replaces, and debounce the input also. The
///     first stops the requests that no code wants, and the second stops the requests before they
///     start.
///
///     <c>deadline</c> must give a time after the time that it reads. An earlier time is a time
///     that the clock went by, and it fires at the next transaction.
/// </remarks>
/// <param name="timers">The timer system that gives the clock and the alarms.</param>
/// <param name="deadline">
///     The time to fire at, from the time of a firing. For a <c>SecondsTimerSystem</c>, a quiet time
///     of 300 milliseconds is <c>fun now -> now + 0.3</c>.
/// </param>
/// <param name="stream">The stream to debounce.</param>
/// <returns>
///     A stream that fires the last value of <paramref name="stream" />, one time, after a time with
///     no other value.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let debounce (timers: ITimerSystem<'T>) (deadline: 'T -> 'T) (stream: Stream<'A>) =
    StreamExtensionMethodsInternal.DebounceImpl(
        stream,
        timers.Time,
        Func<Cell<'T option>, Stream<'T>>(timers.At),
        Func<'T, 'T option>(fun now -> Some(deadline now)),
        None
    )
