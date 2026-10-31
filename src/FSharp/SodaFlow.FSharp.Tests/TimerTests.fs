module SodaFlow.Tests.Timer

open System
open System.Collections.Generic
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
            do! Expect.Sequence([ 12; 12 ], times)
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
