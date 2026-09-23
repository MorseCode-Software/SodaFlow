module SodaFlow.Tests.Timer

open System
open System.Collections.Generic
open System.Threading
open SodaFlow
open SodaFlow.Time
open TUnit.Core

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
