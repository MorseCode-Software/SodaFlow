module SodaFlow.Tests.Timer

open System
open System.Collections.Generic
open NUnit.Framework
open SodaFlow
open System.Threading
open SodaFlow.Time

[<TestFixture>]
type ``Timer Tests``() =

    [<Test>]
    member __.``Simultaneous Timer Events``() =
        let ts = SystemClockTimerSystem(fun e -> ()) :> ITimerSystem<DateTime>
        let time = ts.Time
        let l = List<DateTime>()
        Transaction.run(fun () ->
            let now = time |> Behavior.sample
            let a1 = ts.At(Cell.constant(Some(now.AddMilliseconds(99.0))))
            let a2 = ts.At(Cell.constant(Some(now.AddMilliseconds(100.0))))
            let a3 = ts.At(Cell.constant(Some(now.AddMilliseconds(100.0))))
            let m = Stream.orElseAll [a1;a2;a3]
            m |> Stream.listenStrong (fun v -> lock l (fun () -> l.Add v)) |> ignore)

        // Wait for the alarms rather than assuming a fixed window is long enough. The alarms are
        // 99ms and 100ms out, so a flat 200ms sleep left about 100ms of slack, and a loaded CI
        // agent overran it, failing with zero events rather than the wrong number - a delayed
        // timer thread, not a coalescing bug. Waiting on the condition makes a slow machine take
        // longer instead of failing.
        //
        // The settle afterward keeps the assertion meaningful: it still has to be exactly two,
        // so a third firing - a2 and a3 failing to coalesce - is caught rather than raced past.
        //
        // The lock is not incidental: l is written from the timer thread and read here, which the
        // original fixed sleep left unsynchronized.
        SpinWait.SpinUntil((fun () -> lock l (fun () -> l.Count >= 2)), TimeSpan.FromSeconds(10.0))
        |> ignore
        Thread.Sleep 100

        lock l (fun () -> Assert.That(l.Count, Is.EqualTo(2)))
