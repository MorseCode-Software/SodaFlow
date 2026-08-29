module SodaFlow.Time

open System
open System.Linq
open System.Collections.Generic
open System.Threading

type ITimer =
    inherit IDisposable
    abstract member Cancel : unit -> unit

type ITimerSystem<'T when 'T : comparison> =
    abstract member Time : Behavior<'T>
    abstract member At : Cell<'T option> -> Stream<'T>

type ITimerSystemImplementation<'T> =
    abstract member Start : (exn -> unit) -> unit
    abstract member SetTimer : 'T -> (unit -> unit) -> ITimer
    abstract member RunTimersTo : 'T -> unit
    abstract member Now : 'T

type private Event<'T> = { Time : 'T; Alarm : StreamSink<'T> }

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
            let listener = t |> Cell.listen (fun o ->
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

[<AbstractClass>]
type TimerSystemImplementationBase<'T when 'T : comparison>() as this =
    let lockObject = obj ()
    let timers = SortedSet<SimpleTimer<'T>> ()

    // Signalled whenever the timer set changes, to wake the timer thread so it can recompute how
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

    abstract member SubtractTimes : 'T -> 'T -> TimeSpan

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

            // Signalled outside the lock. Cancelling the old token source was done while holding
            // it, and cancellation runs its callbacks synchronously, so the waiting loop could
            // resume inline on this thread and re-enter timeUntilNext while the caller still held
            // the lock.
            timersChanged.Set () |> ignore
            upcast timer

        member __.RunTimersTo now = timeUntilNext now |> ignore

        member this.Now = this.Now

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

    override this.Equals(otherObj) =
        match otherObj with
        | :? SimpleTimer<'T> as other -> this.Time = other.Time && this.Seq = other.Seq
        | _ -> false

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

type SystemClockTimerSystem(handleException : exn -> unit) =
    inherit TimerSystem<DateTime>(SystemClockTimerSystemImplementation(), handleException)
    
type private SecondsTimerSystemImplementation() =
    inherit TimerSystemImplementationBase<float>()
    let startTime = DateTime.Now
    override __.SubtractTimes first second = TimeSpan.FromSeconds(first - second)
    override __.Now = (DateTime.Now - startTime).TotalSeconds

type SecondsTimerSystem(handleException : exn -> unit) =
    inherit TimerSystem<float>(SecondsTimerSystemImplementation(), handleException)