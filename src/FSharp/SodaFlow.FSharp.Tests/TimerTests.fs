module SodaFlow.Tests.Timer

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open SodaFlow
open SodaFlow.Time
open TUnit.Core

/// A clock that a test moves, with the timer system over it. A test of a wait on the true clock
/// measures the machine and not the graph.
type private ManualTimer(time: int, callback: unit -> unit) =
    let mutable canceled = false

    member _.Time = time
    member _.Canceled = canceled
    member _.Fire() = callback ()

    interface ITimer with
        member _.Cancel() = canceled <- true
        member _.Dispose() = canceled <- true

/// fireLatestFirst makes RunTimersTo fire the due timers from the latest to the earliest, as two
/// threads that fire timers at the same time can. holdBack names the time of a timer that RunTimersTo
/// does not fire, as an implementation does between the removal of a timer and its callback.
type private ManualImplementation(fireLatestFirst: bool, holdBack: int option) =
    let timers = List<ManualTimer>()
    let mutable now = 0
    let mutable stopped = false

    new() = ManualImplementation(false, None)

    member _.AdvanceTo value = now <- value
    member _.Stopped = stopped

    /// Fires the timer that RunTimersTo kept back, out of any transaction.
    member _.FireHeldBackTimer() =
        let timer = timers |> Seq.find (fun timer -> Some timer.Time = holdBack)
        timers.Remove timer |> ignore
        timer.Fire()

    interface ITimerSystemImplementation<int> with
        member _.Now = now
        member _.Start _ = ()
        member _.Stop() = stopped <- true

        member _.SetTimer time callback =
            let timer = new ManualTimer(time, callback)
            timers.Add timer
            timer :> ITimer

        member _.RunTimersTo value =
            // A copy, because a callback here can set a timer of its own.
            let due =
                timers
                |> Seq.filter (fun timer -> not timer.Canceled && timer.Time <= value && Some timer.Time <> holdBack)
                |> Seq.toList

            for timer in (if fireLatestFirst then List.rev due else due) do
                timers.Remove timer |> ignore
                timer.Fire()

/// A clock over a stopwatch, for the behavior of the base implementation itself after a stop.
type private StopwatchClock() =
    inherit TimerSystemImplementationBase<float>()
    let stopwatch = Stopwatch.StartNew()
    override _.SubtractTimes first second = TimeSpan.FromSeconds(first - second)
    override _.Now = stopwatch.Elapsed.TotalSeconds

/// Moves the clock, and opens a transaction. The timer system reads the clock at the start of a
/// transaction.
let private advanceTo (implementation: ManualImplementation) value =
    implementation.AdvanceTo value
    Transaction.run (fun () -> ())

type ``Timer Tests``() =

    [<Test>]
    member _.``Events get to the graph in the sequence of their times``() =
        task {
            let implementation = ManualImplementation(true, None)
            use timerSystem = new TimerSystem<int>(implementation, (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<int>
            let times = List<int>()
            let alarms = List<int>()

            let l =
                Transaction.run (fun () ->
                    StrongListener.fromList
                        [ timers.Time |> Operational.updates |> Stream.listenStrong times.Add
                          timers.At(Cell.constant (Some 10)) |> Stream.listenStrong alarms.Add
                          timers.At(Cell.constant (Some 11)) |> Stream.listenStrong alarms.Add ])

            advanceTo implementation 12
            l |> StrongListener.unlisten

            do! Expect.Sequence([ 10; 11 ], alarms)
            do! Expect.Sequence([ 10; 11; 12 ], times)
        }

    [<Test>]
    member _.``An event that arrives late fires its alarm and does not move Time back``() =
        task {
            let implementation = ManualImplementation(false, Some 10)
            use timerSystem = new TimerSystem<int>(implementation, (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<int>
            let times = List<int>()
            let alarms = List<int>()

            let l =
                Transaction.run (fun () ->
                    StrongListener.fromList
                        [ timers.Time |> Operational.updates |> Stream.listenStrong times.Add
                          timers.At(Cell.constant (Some 10)) |> Stream.listenStrong alarms.Add ])

            advanceTo implementation 12
            implementation.FireHeldBackTimer()
            l |> StrongListener.unlisten

            do! Expect.Sequence([ 10 ], alarms)
            // The transaction that brings the event sends no time, because the clock reads 12, as before.
            do! Expect.Sequence([ 12 ], times)
        }

    /// A disposed timer system removes its hook and stops its clock. Thus, Time keeps the last value
    /// it had, and an alarm that comes due after the disposal does not fire.
    [<Test>]
    member _.``A disposed timer system stops its clock and sends no alarm``() =
        task {
            let implementation = ManualImplementation()
            use timerSystem = new TimerSystem<int>(implementation, (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<int>
            let alarms = List<int>()

            let l =
                Transaction.run (fun () -> timers.At(Cell.constant (Some 10)) |> Stream.listenStrong alarms.Add)

            advanceTo implementation 5
            timers.Dispose()
            advanceTo implementation 12
            l |> StrongListener.unlisten

            do! Expect.True implementation.Stopped
            do! Expect.Equal(5, timers.Time |> Behavior.sample)
            do! Expect.Equal(0, alarms.Count)

            // A second disposal does nothing.
            timers.Dispose()
        }

    [<Test>]
    member _.``A clock with a resolution moves Time once per step``() =
        task {
            let start = DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)
            let mutable now = start

            use timerSystem =
                new SystemClockTimerSystem((fun () -> now), TimeSpan.FromMilliseconds 1.0, (fun _ -> ()))

            let timers = timerSystem :> ITimerSystem<DateTime>
            let times = List<DateTime>()
            let l = timers.Time |> Operational.updates |> Stream.listenStrong times.Add

            for step in 1..5 do
                now <- start.AddTicks(int64 step * 4L * TimeSpan.TicksPerMillisecond / 10L)
                Transaction.run (fun () -> ())

            l |> StrongListener.unlisten

            do! Expect.Sequence([ start.AddMilliseconds 1.0; start.AddMilliseconds 2.0 ], times)
            do! Expect.Equal(start.AddMilliseconds 2.0, timers.Time |> Behavior.sample)
        }

    [<Test>]
    member _.``A negative resolution is refused``() =
        task {
            let negative = TimeSpan.FromTicks -1L

            do!
                Expect.Throws<ArgumentOutOfRangeException>(fun () ->
                    new SecondsTimerSystem(negative, (fun _ -> ())) |> ignore)

            do!
                Expect.Throws<ArgumentOutOfRangeException>(fun () ->
                    new SystemClockTimerSystem(negative, (fun _ -> ())) |> ignore)

            do!
                Expect.Throws<ArgumentOutOfRangeException>(fun () ->
                    new UtcClockTimerSystem(negative, (fun _ -> ())) |> ignore)
        }

    [<Test>]
    member _.``Time follows a clock that goes back``() =
        task {
            let implementation = ManualImplementation()
            use timerSystem = new TimerSystem<int>(implementation, (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<int>
            let times = List<int>()

            let l = timers.Time |> Operational.updates |> Stream.listenStrong times.Add
            advanceTo implementation 12
            advanceTo implementation 5
            l |> StrongListener.unlisten

            do! Expect.Sequence([ 12; 5 ], times)
        }

    [<Test>]
    member _.``Simultaneous Timer Events``() =
        task {
            use timerSystem = new SystemClockTimerSystem(fun _ -> ())
            let ts = timerSystem :> ITimerSystem<DateTime>
            let time = ts.Time
            let l = List<DateTime>()

            Transaction.run (fun () ->
                let now = time |> Behavior.sample
                let a1 = ts.At(Cell.constant (Some(now.AddMilliseconds(99.0))))
                let a2 = ts.At(Cell.constant (Some(now.AddMilliseconds(100.0))))
                let a3 = ts.At(Cell.constant (Some(now.AddMilliseconds(100.0))))
                let m = Stream.orElseAll [ a1; a2; a3 ]
                m |> Stream.listenStrong (fun v -> lock l (fun () -> l.Add v)) |> ignore)

            // This waits for the alarms, and does not use a constant window. The alarms are 99ms and 100ms
            // in the future. Thus, a constant sleep of 200ms left approximately 100ms of margin, and a CI
            // agent with a high load used more than that margin. The test then gave zero events, and not
            // an incorrect count. The cause was a late timer thread, and not an error in the coalesce. A
            // wait on the condition makes a slow machine use more time, and does not fail.
            //
            // The wait after this keeps the assertion useful. The count must stay at two. Thus, the test
            // sees a third firing when a2 and a3 do not coalesce, and does not complete before that
            // firing.
            //
            // The lock is necessary, because the timer thread writes l and this code reads it. The initial
            // test with a constant sleep had no lock.
            SpinWait.SpinUntil((fun () -> lock l (fun () -> l.Count >= 2)), TimeSpan.FromSeconds(10.0))
            |> ignore

            Thread.Sleep 100

            let count = lock l (fun () -> l.Count)

            do! Expect.Equal(2, count)
        }

    [<Test>]
    member _.``debounce fires the last value after a quiet time``() =
        task {
            let implementation = ManualImplementation()
            use timerSystem = new TimerSystem<int>(implementation, (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<int>
            let keys = StreamSink.create<string> ()
            let seen = List<string>()

            let advanceTo value =
                implementation.AdvanceTo value
                // The timer system reads the clock at the start of a transaction.
                Transaction.run (fun () -> ())

            let l =
                Transaction.run (fun () ->
                    keys
                    |> Time.debounce timers (fun now -> now + 10)
                    |> Stream.listenStrong seen.Add)

            keys |> StreamSink.send "a"
            advanceTo 3
            keys |> StreamSink.send "ab"
            advanceTo 6
            keys |> StreamSink.send "abc"

            do! Expect.Equal(0, seen.Count)

            // The last firing was at 6, thus the alarm is at 16.
            advanceTo 16
            do! Expect.Sequence([ "abc" ], seen)

            // The debounce works again after it fired.
            keys |> StreamSink.send "z"
            advanceTo 26
            do! Expect.Sequence([ "abc"; "z" ], seen)

            l |> StrongListener.unlisten
        }

type ``System Clock Timer System Tests``() =

    /// The system clock timer system takes any clock. Time follows that clock, and a transaction
    /// that starts after the clock passes a timer fires it.
    [<Test>]
    member _.``the system clock timer system takes its clock``() =
        task {
            let mutable now = DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc)
            use timerSystem = new SystemClockTimerSystem((fun () -> now), (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<DateTime>
            let alarms = List<DateTime>()

            let listener =
                timers.At(Cell.constant (Some(now.AddMinutes 5.0))) |> Stream.listenStrong alarms.Add

            Transaction.run (fun () -> ())

            do! Expect.Equal(now, Behavior.sample timers.Time)
            do! Expect.Equal(0, alarms.Count)

            now <- now.AddMinutes 5.0
            Transaction.run (fun () -> ())
            listener.Unlisten()

            do! Expect.Equal(now, Behavior.sample timers.Time)
            do! Expect.Sequence([ now ], alarms)
        }

    /// The UTC timer system measures time in UTC, and the default system clock timer system in
    /// local time. Each value of Time says which.
    [<Test>]
    member _.``the UTC clock timer system counts in UTC and the system clock one in local time``() =
        task {
            use utcSystem = new UtcClockTimerSystem(fun _ -> ())
            use localSystem = new SystemClockTimerSystem(fun _ -> ())
            let utc = utcSystem :> ITimerSystem<DateTime>
            let local = localSystem :> ITimerSystem<DateTime>
            Transaction.run (fun () -> ())

            do! Expect.Equal(DateTimeKind.Utc, (Behavior.sample utc.Time).Kind)
            do! Expect.Equal(DateTimeKind.Local, (Behavior.sample local.Time).Kind)
        }

    // Stop ends the timer thread and waits for it. Thus, a timer set before the stop does not fire
    // after it. A timer set after the stop never fires, and a handle for it tells the caller
    // nothing. Thus, SetTimer throws instead.
    [<Test>]
    member _.``A stopped implementation refuses a timer and fires none``() =
        task {
            let clock = StopwatchClock() :> ITimerSystemImplementation<float>
            let fired = ref false
            clock.Start(fun _ -> ())
            use _timer = clock.SetTimer (clock.Now + 1000.0) (fun () -> fired.Value <- true)
            clock.Stop()

            do!
                Expect.Throws<ObjectDisposedException>(fun () ->
                    clock.SetTimer (clock.Now - 1.0) (fun () -> fired.Value <- true) |> ignore)

            clock.RunTimersTo(clock.Now + 2000.0)
            do! Expect.False fired.Value

            // A second stop does nothing.
            clock.Stop()
        }

    // At sets its timer in the transaction that gives the cell a time. Thus, that transaction fails
    // with the exception of the stopped implementation. That is the construction when the cell
    // holds a time, and the later send that gives it one.
    [<Test>]
    member _.``A disposed timer system refuses an alarm``() =
        task {
            let timerSystem = new TimerSystem<float>(StopwatchClock(), (fun _ -> ()))
            let timers = timerSystem :> ITimerSystem<float>
            timers.Dispose()

            do! Expect.Throws<ObjectDisposedException>(fun () -> timers.At(Cell.constant (Some 1.0)) |> ignore)

            let alarm = sinkC None
            let _at = timers.At alarm
            do! Expect.Throws<ObjectDisposedException>(fun () -> alarm |> sendC (Some 1.0))
        }
