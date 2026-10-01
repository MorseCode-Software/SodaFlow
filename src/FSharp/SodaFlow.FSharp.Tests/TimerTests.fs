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

type private ManualImplementation() =
    let timers = List<ManualTimer>()
    let mutable now = 0

    member _.AdvanceTo value = now <- value

    interface ITimerSystemImplementation<int> with
        member _.Now = now
        member _.Start _ = ()

        member _.SetTimer time callback =
            let timer = new ManualTimer(time, callback)
            timers.Add timer
            timer :> ITimer

        member _.RunTimersTo value =
            // A copy, because a callback here can set a timer of its own.
            let due =
                timers
                |> Seq.filter (fun timer -> not timer.Canceled && timer.Time <= value)
                |> Seq.toList

            for timer in due do
                timers.Remove timer |> ignore
                timer.Fire()

type ``Timer Tests``() =

    [<Test>]
    member _.``Simultaneous Timer Events``() =
        task {
            let ts = SystemClockTimerSystem(fun _ -> ()) :> ITimerSystem<DateTime>
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
            let timers = TimerSystem<int>(implementation, (fun _ -> ())) :> ITimerSystem<int>
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
