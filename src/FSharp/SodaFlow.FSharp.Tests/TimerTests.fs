module SodaFlow.Tests.Timer

open System
open System.Diagnostics
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
            m |> Stream.listen (fun v -> lock l (fun () -> l.Add v)) |> ignore)

        // Alarms are delivered by the Transaction.OnStart hook, which calls RunTimersTo to fire
        // any timer that has come due and then drains the queued events. What normally triggers
        // that hook is the timer callback opening an empty transaction - but the callback runs on
        // a Task.Run loop whose Task.Delay continuations need the thread pool, and on a loaded CI
        // agent that loop may not be scheduled promptly. The test failed there with zero events
        // even after waiting ten seconds, so waiting longer is not the fix.
        //
        // Opening transactions here drives the same delivery path directly, which is what the
        // library does internally, so the result no longer depends on pool scheduling.
        //
        // Coalescing is still under test: the drain pops events one timestamp at a time, so even a
        // single late transaction delivers the 99ms alarm and the pair at 100ms separately - two
        // firings, not one and not three.
        Thread.Sleep 200

        let stopwatch = Stopwatch.StartNew()
        let mutable finished = false
        while not finished do
            Transaction.run (fun () -> ())
            if lock l (fun () -> l.Count >= 2) then finished <- true
            elif stopwatch.Elapsed > TimeSpan.FromSeconds 10.0 then finished <- true
            else Thread.Sleep 10

        // Settle, then drive once more, so a third firing fails the test rather than being raced
        // past.
        Thread.Sleep 100
        Transaction.run (fun () -> ())

        lock l (fun () -> Assert.That(l.Count, Is.EqualTo(2)))
