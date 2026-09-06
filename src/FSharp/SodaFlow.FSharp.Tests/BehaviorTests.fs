module SodaFlow.Tests.Behavior

open System
open System.Collections.Generic
open System.Threading.Tasks
open SodaFlow
open TUnit.Core

type private TestObject(n1: int, n2: int) =
    let removeStreamSink = sinkS ()
    let changeNumber1StreamSink = sinkS ()
    let changeNumber2StreamSink = sinkS ()
    let number1Cell = changeNumber1StreamSink |> holdS n1
    let number2Cell = changeNumber2StreamSink |> holdS n2

    member val RemoveStream: Stream<unit> = upcast removeStreamSink
    member _.Number1Cell = number1Cell
    member _.Number2Cell = number2Cell
    member val BothNumbersCell = (number1Cell, number2Cell) |> lift2C (fun n1 n2 -> (n1, n2))

    member _.Remove() = removeStreamSink |> sendS ()
    member _.ChangeNumber1 n = changeNumber1StreamSink |> sendS n
    member _.ChangeNumber2 n = changeNumber2StreamSink |> sendS n

type private Sc =
    { a: char option
      b: char option
      sw: Cell<char> option }

type private Sc2 = { c: CellSink<int> }

type private Ss =
    { a: char
      b: char
      sw: Stream<char> option }

type private Ss2 = { s: StreamSink<int> }

type private Test(initialValue: int) =
    member val Value = sinkB initialValue

type ``Behavior Tests``() =

    [<Test>]
    member _.``Test Hold``() =
        task {
            let s = sinkS ()
            let c = s |> holdS 0
            let out = List<_>()
            let l = c |> listenStrongC out.Add
            s |> sendS 2
            s |> sendS 9
            l |> unlistenL
            do! Expect.Sequence([ 0; 2; 9 ], out)
        }

    [<Test>]
    member _.``Test Send Null``() =
        task {
            let c = sinkC ""
            let out = List<_>()
            let l = c |> listenStrongC out.Add
            c |> sendC "0"
            c |> sendC null
            c |> sendC "1"
            l |> unlistenL
            do! Expect.Sequence([ ""; "0"; null; "1" ], out)
        }

    [<Test>]
    member _.``Test Hold Updates``() =
        task {
            let s = sinkS ()
            let c = s |> holdS 0
            let out = List<_>()
            let l = c |> updatesC |> listenStrongS out.Add
            s |> sendS 2
            s |> sendS 9
            l |> unlistenL
            do! Expect.Sequence([ 2; 9 ], out)
        }

    [<Test>]
    member _.``Test Snapshot``() =
        task {
            let b = sinkB 0
            let trigger = sinkS ()
            let out = List<_>()

            let l =
                trigger
                |> snapshotB b (fun x y -> (string x) + " " + (string y))
                |> listenStrongS out.Add

            trigger |> sendS 100L
            b |> sendB 2
            trigger |> sendS 200L
            b |> sendB 9
            b |> sendB 1
            trigger |> sendS 300L
            l |> unlistenL
            do! Expect.Sequence([ "100 0"; "200 2"; "300 1" ], out)
        }

    [<Test>]
    member _.``Test ListenStrong``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = c |> listenStrongC out.Add
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 9; 2; 7 ], out)
        }

    [<Test>]
    member _.``Test ListenOnce``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = runT (fun () -> c |> valuesC |> listenOnceS out.Add)
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 9 ], out)
        }

    [<Test>]
    member _.``Test ListenOnce Updates``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = runT (fun () -> c |> updatesC |> listenOnceS out.Add)
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 2 ], out)
        }

    [<Test>]
    member _.``Test ListenOnceAsync``() =
        task {
            let c = sinkC 9
            let! result = runT (fun () -> c |> valuesC |> listenOnceAsyncS)
            c |> sendC 2
            c |> sendC 7
            do! Expect.Equal(9, result)
        }

    [<Test>]
    member _.``Test Updates``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let _l = c |> updatesC |> listenStrongS out.Add
            c |> sendC 2
            c |> sendC 7
            do! Expect.Sequence([ 2; 7 ], out)
        }

    [<Test>]
    member _.``Test Values``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let _l = runT (fun () -> c |> valuesC |> listenStrongS out.Add)
            c |> sendC 2
            c |> sendC 7
            do! Expect.Sequence([ 9; 2; 7 ], out)
        }

    [<Test>]
    member _.``Test CellLoop Complex``() =
        task {
            let s = sinkS ()
            let addItemStreamSink = sinkS ()
            let removeItemsStreamSink = sinkS ()

            let listCell =
                loopWithNoCapturesC (fun listCell ->
                    ((addItemStreamSink, (s |> mapS (fun v -> v, v)))
                     |> orElseS
                     |> mapS (fun o -> (fun c -> Seq.append c [ (TestObject o) ] |> Array.ofSeq)),
                     removeItemsStreamSink
                     |> mapS (fun o -> (fun c -> c |> Seq.except o |> Array.ofSeq)))
                    |> mergeS (fun f g -> (fun c -> f c |> g))
                    |> snapshotC listCell id
                    |> holdS Array.empty)

            let l2 =
                runT (fun () ->
                    listCell
                    |> valuesC
                    |> listenStrongS (fun c ->
                        postT (fun () ->
                            if c.Length > 0 then
                                let n1, n2 = (c |> Array.last).BothNumbersCell |> sampleC

                                if n1 = 9 && n2 = 9 then
                                    addItemStreamSink |> sendS (0, 0))))

            let _yo =
                Seq.map (fun (o: TestObject) -> o.RemoveStream |> mapToS [| o |])
                >> mergeAllS Array.append

            let l3 =
                runT (fun () ->
                    listCell
                    |> mapC (Seq.map (fun o -> o.RemoveStream |> mapToS [| o |]) >> mergeAllS Array.append)
                    |> switchS
                    |> listenStrongS (fun o -> postT (fun () -> removeItemsStreamSink |> sendS o)))

            let l4 =
                runT (fun () ->
                    listCell
                    |> mapC (fun c ->
                        if c.Length > 0 then
                            ((c |> Array.last).Number1Cell, (c |> Array.last).Number2Cell)
                            |> lift2C (fun x y -> x = 9 && y = 9)
                            |> updatesC
                        else
                            neverS ())
                    |> switchS
                    |> filterS id
                    |> listenStrongS (fun _ -> postT (fun () -> addItemStreamSink |> sendS (0, 0))))

            let out = List<_>()

            let l =
                listCell
                |> mapC (fun c ->
                    c
                    |> Seq.map (fun o -> (o.Number1Cell, o.Number2Cell) |> lift2C (fun x y -> x, y))
                    |> liftAllC id)
                |> switchC
                |> listenStrongC out.Add

            addItemStreamSink |> sendS (5, 2)
            addItemStreamSink |> sendS (9, 2)
            (listCell |> sampleC |> (fun o -> o[0])).Remove()
            addItemStreamSink |> sendS (2, 9)
            (listCell |> sampleC |> (fun o -> o[1])).ChangeNumber1 9
            addItemStreamSink |> sendS (9, 9)
            s |> sendS 5
            s |> sendS 9

            runT (fun () ->
                addItemStreamSink |> sendS (5, 5)
                s |> sendS 5)

            (listCell |> sampleC |> (fun o -> o[8])).ChangeNumber2 9
            (listCell |> sampleC |> (fun o -> o[8])).ChangeNumber1 9
            l |> unlistenL
            l2 |> unlistenL
            l3 |> unlistenL
            l4 |> unlistenL

            let expected =
                [| Array.empty
                   [| (5, 2) |]
                   [| (5, 2); (9, 2) |]
                   [| (9, 2) |]
                   [| (9, 2); (2, 9) |]
                   [| (9, 2); (9, 9) |]
                   [| (9, 2); (9, 9); (0, 0) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0); (5, 5) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0); (5, 5); (9, 9) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0); (5, 5); (9, 9); (0, 0) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0); (5, 5); (9, 9); (0, 0); (5, 5) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0); (5, 5); (9, 9); (0, 0); (5, 9) |]
                   [| (9, 2); (9, 9); (0, 0); (9, 9); (0, 0); (5, 5); (9, 9); (0, 0); (9, 9) |]
                   [| (9, 2)
                      (9, 9)
                      (0, 0)
                      (9, 9)
                      (0, 0)
                      (5, 5)
                      (9, 9)
                      (0, 0)
                      (9, 9)
                      (0, 0) |] |]

            do! Expect.Equal(expected.Length, out.Count)

            for i in 0 .. expected.Length - 1 do
                let e = expected[i]
                let o = out[i]
                do! Expect.Equal(e.Length, o.Count)

                for j in 0 .. e.Length - 1 do
                    do! Expect.Equal(e[j], o[j])
        }

    [<Test>]
    member _.``Test CellLoop``() =
        task {
            let s = sinkS ()
            let cell = loopWithNoCapturesC (fun cell -> s |> snapshotC cell (+) |> holdS 1)
            let out = List<_>()
            let l = cell |> listenStrongC out.Add
            s |> sendS 3
            s |> sendS 4
            s |> sendS 7
            s |> sendS 8
            l |> unlistenL
            do! Expect.Sequence([ 1; 4; 8; 15; 23 ], out)
        }

    [<Test>]
    member _.``Test CellLoop Throws Exception``() =
        task {
            let actual =
                try
                    let s = sinkS ()

                    let cell =
                        loopWithNoCapturesC (fun cell ->
                            (cell |> updatesC |> filterS (fun v -> v % 2 = 0) |> mapS ((+) 1), s)
                            |> mergeS (fun _ r -> r)
                            |> holdS 1)

                    let out = List<_>()
                    let l = cell |> listenStrongC out.Add
                    s |> sendS 3
                    s |> sendS 4
                    s |> sendS 7
                    s |> sendS 8
                    l |> unlistenL
                    None
                with
                | :? AggregateException as e ->
                    e.InnerExceptions
                    |> Seq.tryFind (fun e -> e.Message = "A dependency cycle was detected.")
                | e -> Some e

            do!
                actual
                |> assertExceptionExists (fun e -> Expect.Equal("A dependency cycle was detected.", e.Message))
        }

    [<Test>]
    member _.``Test CellLoop SwitchS``() =
        task {
            let addStreamSink = sinkS ()

            let cell: Cell<TestObject[]> =
                loopWithNoCapturesC (fun cell ->
                    (cell
                     |> mapC (Seq.map (fun o -> o.RemoveStream |> mapToS [| o |]) >> mergeAllS Array.append)
                     |> switchS
                     |> mapS Array.except,
                     (addStreamSink |> mapS Array.singleton |> mapS Array.append))
                    |> mergeS (>>)
                    |> snapshotC cell (<|)
                    |> holdS Array.empty)

            let out = List<_>()
            let l = cell |> listenStrongC (out.Add << Array.length)
            let t1 = TestObject(1, 1)
            addStreamSink |> sendS t1
            let t2 = TestObject(2, 2)
            addStreamSink |> sendS t2
            let t3 = TestObject(3, 3)
            addStreamSink |> sendS t3
            t2.Remove()
            let t4 = TestObject(4, 4)

            runT (fun () ->
                addStreamSink |> sendS t4
                t3.Remove())

            let t5 = TestObject(5, 5)
            addStreamSink |> sendS t5
            l |> unlistenL
            do! Expect.Sequence([ 0; 1; 2; 3; 2; 2; 3 ], out)
        }

    [<Test>]
    member _.``Test Cell Values``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = runT (fun () -> c |> valuesC |> listenStrongS out.Add)
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 9; 2; 7 ], out)
        }

    // A Values stream obtained inside a transaction that has already sent fires with the value sent
    // in that transaction, not the one the cell held when it opened - and a second Values obtained
    // from within the first one's handler must see the same thing, rather than missing it for
    // having been attached partway through.
    [<Test>]
    member _.``Test Values Attached Late``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let mutable l2 = None

            let l =
                runT (fun () ->
                    c |> sendC 5

                    c
                    |> valuesC
                    |> listenStrongS (fun _ ->
                        if l2.IsNone then
                            l2 <- Some(c |> valuesC |> listenStrongS out.Add)))

            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            l2 |> Option.iter unlistenL
            do! Expect.Sequence([ 5; 2; 7 ], out)
        }

    [<Test>]
    member _.``Test Cell Values No Transaction``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = c |> valuesC |> listenStrongS out.Add
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 2; 7 ], out)
        }

    [<Test>]
    member _.``Test Value Then Map``() =
        task {
            let b = sinkB 9
            let out = List<_>()

            let l =
                runT (fun () -> b |> Operational.value |> mapS ((+) 100) |> listenStrongS out.Add)

            b |> sendB 2
            b |> sendB 7
            l |> unlistenL
            do! Expect.Sequence([ 109; 102; 107 ], out)
        }

    [<Test>]
    member _.``Test Cell Values Then Map``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = runT (fun () -> c |> valuesC |> mapS ((+) 100) |> listenStrongS out.Add)
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 109; 102; 107 ], out)
        }

    [<Test>]
    member _.``Test Value Then Merge``() =
        task {
            let b1 = sinkB 9
            let b2 = sinkB 2
            let out = List<_>()

            let l =
                runT (fun () ->
                    (b1 |> Operational.value, b2 |> Operational.value)
                    |> mergeS (+)
                    |> listenStrongS out.Add)

            b1 |> sendB 1
            b2 |> sendB 4

            runT (fun () ->
                b1 |> sendB 7
                b2 |> sendB 5)

            l |> unlistenL
            do! Expect.Sequence([ 11; 1; 4; 12 ], out)
        }

    [<Test>]
    member _.``Test Cell Values Then Merge``() =
        task {
            let c1 = sinkC 9
            let c2 = sinkC 2
            let out = List<_>()

            let l =
                runT (fun () -> (c1 |> valuesC, c2 |> valuesC) |> mergeS (+) |> listenStrongS out.Add)

            c1 |> sendC 1
            c2 |> sendC 4

            runT (fun () ->
                c1 |> sendC 7
                c2 |> sendC 5)

            l |> unlistenL
            do! Expect.Sequence([ 11; 1; 4; 12 ], out)
        }

    [<Test>]
    member _.``Test Value Then Filter``() =
        task {
            let b = sinkB 9
            let out = List<_>()

            let l =
                runT (fun () -> b |> Operational.value |> filterS (fun x -> x % 2 <> 0) |> listenStrongS out.Add)

            b |> sendB 2
            b |> sendB 7
            l |> unlistenL
            do! Expect.Sequence([ 9; 7 ], out)
        }

    [<Test>]
    member _.``Test Cell Values Then Filter``() =
        task {
            let c = sinkC 9
            let out = List<_>()

            let l =
                runT (fun () -> c |> valuesC |> filterS (fun x -> x % 2 <> 0) |> listenStrongS out.Add)

            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 9; 7 ], out)
        }

    [<Test>]
    member _.``Test Value Then Once``() =
        task {
            let b = sinkB 9
            let out = List<_>()
            let l = runT (fun () -> b |> Operational.value |> onceS |> listenStrongS out.Add)
            b |> sendB 2
            b |> sendB 7
            l |> unlistenL
            do! Expect.Sequence([ 9 ], out)
        }

    [<Test>]
    member _.``Test Cell Values Then Once``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let l = runT (fun () -> c |> valuesC |> onceS |> listenStrongS out.Add)
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 9 ], out)
        }

    [<Test>]
    member _.``Test Value Then Late ListenStrong``() =
        task {
            let b = sinkB 9
            let out = List<_>()
            let value = b |> Operational.value
            b |> sendB 8
            let l = value |> listenStrongS out.Add
            b |> sendB 2
            b |> sendB 7
            l |> unlistenL
            do! Expect.Sequence([ 2; 7 ], out)
        }

    [<Test>]
    member _.``Test Cell Values Then Late ListenStrong``() =
        task {
            let c = sinkC 9
            let out = List<_>()
            let value = c |> valuesC
            c |> sendC 8
            let l = value |> listenStrongS out.Add
            c |> sendC 2
            c |> sendC 7
            l |> unlistenL
            do! Expect.Sequence([ 2; 7 ], out)
        }

    [<Test>]
    member _.``Test Map``() =
        task {
            let c = sinkC 6
            let out = List<_>()
            let l = c |> mapC string |> listenStrongC out.Add
            c |> sendC 8
            l |> unlistenL
            do! Expect.Sequence([ "6"; "8" ], out)
        }

    [<Test>]
    member _.``Test Map Late ListenStrong``() =
        task {
            let c = sinkC 6
            let out = List<_>()
            let cm = c |> mapC string
            c |> sendC 2
            let l = cm |> listenStrongC out.Add
            c |> sendC 8
            l |> unlistenL
            do! Expect.Sequence([ "2"; "8" ], out)
        }

    [<Test>]
    member _.``Test Calm``() =
        task {
            let c = sinkC 2
            let out = List<_>()
            let l = c |> calmC |> listenStrongC out.Add
            c |> sendC 2
            c |> sendC 2
            c |> sendC 4
            c |> sendC 2
            c |> sendC 4
            c |> sendC 4
            c |> sendC 2
            c |> sendC 2
            l |> unlistenL
            do! Expect.Sequence([ 2; 4; 2; 4; 2 ], out)
        }

    [<Test>]
    member _.``Test Calm 2``() =
        task {
            let c = sinkC 2
            let out = List<_>()
            let l = c |> calmC |> listenStrongC out.Add
            c |> sendC 4
            c |> sendC 2
            c |> sendC 4
            c |> sendC 4
            c |> sendC 2
            c |> sendC 2
            l |> unlistenL
            do! Expect.Sequence([ 2; 4; 2; 4; 2 ], out)
        }

    [<Test>]
    member _.``Test Apply``() =
        task {
            let cf = sinkC (fun x -> "1 " + string x)
            let ca = sinkC 5L
            let out = List<_>()
            let l = ca |> applyC cf |> listenStrongC out.Add
            cf |> sendC (fun x -> "12 " + string x)
            ca |> sendC 6L
            l |> unlistenL
            do! Expect.Sequence([ "1 5"; "12 5"; "12 6" ], out)
        }

    [<Test>]
    member _.``Test Lift``() =
        task {
            let c1 = sinkC 1
            let c2 = sinkC 5L
            let out = List<_>()

            let l =
                (c1, c2)
                |> lift2C (fun x y -> string x + " " + string y)
                |> listenStrongC out.Add

            c1 |> sendC 12
            c2 |> sendC 6L
            l |> unlistenL
            do! Expect.Sequence([ "1 5"; "12 5"; "12 6" ], out)
        }

    [<Test>]
    member _.``Test Lift Glitch``() =
        task {
            let c1 = sinkC 1
            let c3 = c1 |> mapC ((*) 3)
            let c5 = c1 |> mapC ((*) 5)
            let c = (c3, c5) |> lift2C (fun x y -> string x + " " + string y)
            let out = List<_>()
            let l = c |> listenStrongC out.Add
            c1 |> sendC 2
            l |> unlistenL
            do! Expect.Sequence([ "3 5"; "6 10" ], out)
        }

    [<Test>]
    member _.``Test Lift From Simultaneous``() =
        task {
            let c1, c2 =
                runT (fun () ->
                    let c1 = sinkC 3
                    let c2 = sinkC 5
                    c2 |> sendC 7
                    (c1, c2))

            let out = List<_>()
            let l = (c1, c2) |> lift2C (+) |> listenStrongC out.Add
            l |> unlistenL
            do! Expect.Sequence([ 10 ], out)
        }

    [<Test>]
    member _.``Test Hold Is Delayed``() =
        task {
            let s = sinkS ()
            let h = s |> holdS 0
            let pair = s |> snapshotC h (fun a b -> string a + " " + string b)
            let out = List<_>()
            let l = pair |> listenStrongS out.Add
            s |> sendS 2
            s |> sendS 3
            l |> unlistenL
            do! Expect.Sequence([ "2 0"; "3 2" ], out)
        }

    [<Test>]
    member _.``Test SwitchC``() =
        task {
            let ssc = sinkS<Sc> ()
            let ca = ssc |> mapS (fun s -> s.a) |> filterSomeS |> holdS 'A'
            let cb = ssc |> mapS (fun s -> s.b) |> filterSomeS |> holdS 'a'
            let csw = ssc |> mapS (fun s -> s.sw) |> filterSomeS |> holdS ca
            let co = csw |> switchC
            let out = List<_>()
            let l = co |> listenStrongC out.Add

            ssc
            |> sendS
                { a = Option.Some 'B'
                  b = Option.Some 'b'
                  sw = Option.None }

            ssc
            |> sendS
                { a = Option.Some 'C'
                  b = Option.Some 'c'
                  sw = Option.Some cb }

            ssc
            |> sendS
                { a = Option.Some 'D'
                  b = Option.Some 'd'
                  sw = Option.None }

            ssc
            |> sendS
                { a = Option.Some 'E'
                  b = Option.Some 'e'
                  sw = Option.Some ca }

            ssc
            |> sendS
                { a = Option.Some 'F'
                  b = Option.Some 'f'
                  sw = Option.None }

            ssc
            |> sendS
                { a = Option.None
                  b = Option.None
                  sw = Option.Some cb }

            ssc
            |> sendS
                { a = Option.None
                  b = Option.None
                  sw = Option.Some ca }

            ssc
            |> sendS
                { a = Option.Some 'G'
                  b = Option.Some 'g'
                  sw = Option.Some cb }

            ssc
            |> sendS
                { a = Option.Some 'H'
                  b = Option.Some 'h'
                  sw = Option.Some ca }

            ssc
            |> sendS
                { a = Option.Some 'I'
                  b = Option.Some 'i'
                  sw = Option.Some ca }

            l |> unlistenL
            do! Expect.Sequence([ 'A'; 'B'; 'c'; 'd'; 'E'; 'F'; 'f'; 'F'; 'g'; 'H'; 'I' ], out)
        }

    [<Test>]
    member _.``Test SwitchC Simultaneous``() =
        task {
            let sc1 = { c = sinkC 0 }
            let csc = sinkC sc1
            let co = csc |> mapC (fun b -> b.c) |> switchC
            let out = List<_>()
            let l = co |> listenStrongC out.Add
            let sc2 = { c = sinkC 3 }
            let sc3 = { c = sinkC 4 }
            let sc4 = { c = sinkC 7 }
            sc1.c |> sendC 1
            sc1.c |> sendC 2
            csc |> sendC sc2
            sc1.c |> sendC 3
            sc2.c |> sendC 4
            sc3.c |> sendC 5
            csc |> sendC sc3
            sc3.c |> sendC 6
            sc3.c |> sendC 7

            runT (fun () ->
                sc3.c |> sendC 2
                csc |> sendC sc4
                sc4.c |> sendC 8)

            sc4.c |> sendC 9
            l |> unlistenL
            do! Expect.Sequence([ 0; 1; 2; 3; 4; 5; 6; 7; 8; 9 ], out)
        }

    [<Test>]
    member _.``Test SwitchS``() =
        task {
            let sss = sinkS ()
            let sa = sss |> mapS (fun s -> s.a)
            let sb = sss |> mapS (fun s -> s.b)
            let csw = sss |> mapS (fun s -> s.sw) |> filterSomeS |> holdS sa
            let so = csw |> switchS
            let out = List<_>()
            let l = so |> listenStrongS out.Add
            sss |> sendS { a = 'A'; b = 'a'; sw = Option.None }
            sss |> sendS { a = 'B'; b = 'b'; sw = Option.None }

            sss
            |> sendS
                { a = 'C'
                  b = 'c'
                  sw = Option.Some sb }

            sss |> sendS { a = 'D'; b = 'd'; sw = Option.None }

            sss
            |> sendS
                { a = 'E'
                  b = 'e'
                  sw = Option.Some sa }

            sss |> sendS { a = 'F'; b = 'f'; sw = Option.None }

            sss
            |> sendS
                { a = 'G'
                  b = 'g'
                  sw = Option.Some sb }

            sss
            |> sendS
                { a = 'H'
                  b = 'h'
                  sw = Option.Some sa }

            sss
            |> sendS
                { a = 'I'
                  b = 'i'
                  sw = Option.Some sa }

            l |> unlistenL
            do! Expect.Sequence([ 'A'; 'B'; 'C'; 'd'; 'e'; 'F'; 'G'; 'h'; 'I' ], out)
        }

    [<Test>]
    member _.``Test SwitchS Simultaneous``() =
        task {
            let ss1 = { s = sinkS () }
            let css = sinkB ss1
            let so = css |> mapB (fun b -> b.s) |> switchSB
            let out = List<_>()
            let l = so |> listenStrongS out.Add
            let ss2 = { s = sinkS () }
            let ss3 = { s = sinkS () }
            let ss4 = { s = sinkS () }
            ss1.s |> sendS 0
            ss1.s |> sendS 1
            ss1.s |> sendS 2
            css |> sendB ss2
            ss1.s |> sendS 7
            ss2.s |> sendS 3
            ss2.s |> sendS 4
            ss3.s |> sendS 2
            css |> sendB ss3
            ss3.s |> sendS 5
            ss3.s |> sendS 6
            ss3.s |> sendS 7

            runT (fun () ->
                ss3.s |> sendS 8
                css |> sendB ss4
                ss4.s |> sendS 2)

            ss4.s |> sendS 9
            l |> unlistenL
            do! Expect.Sequence([ 0; 1; 2; 3; 4; 5; 6; 7; 8; 9 ], out)
        }

    [<Test>]
    member _.``Test Lift List``() =
        task {
            let cellSinks = List.init 50 (fun _ -> sinkC 1)
            let sum = cellSinks |> liftAllC Seq.sum
            let out = List<_>()

            let _ =
                (use _l = sum |> listenStrongC out.Add
                 cellSinks[4] |> sendC 5
                 cellSinks[5] |> sendC 5

                 runT (fun () ->
                     cellSinks[9] |> sendC 5
                     cellSinks[17] |> sendC 5
                     cellSinks[41] |> sendC 5
                     cellSinks[48] |> sendC 5))

            do! Expect.Sequence([ 50; 54; 58; 74 ], out)
        }

    [<Test>]
    member _.``Test Lift Loop List``() =
        task {
            let struct (c, s) =
                runT (fun () ->
                    let struct (_, (c, s)) =
                        loopC (fun c1 ->
                            let s1 = sinkC 1

                            let struct (_, (c, s)) =
                                loopC (fun c2 ->
                                    let s2 = sinkC 1

                                    let struct (_, (c, s)) =
                                        loopC (fun c3 ->
                                            let s3 = sinkC 1

                                            let struct (_, (c, s)) =
                                                loopC (fun c4 ->
                                                    let s4 = sinkC 1

                                                    let struct (_, (c, s)) =
                                                        loopC (fun c5 ->
                                                            let s5 = sinkC 1

                                                            struct (s5,
                                                                    ([| c1; c2; c3; c4; c5 |] |> liftAllC Seq.sum, s5)))

                                                    struct (s4, (c, [| s; s4 |])))

                                            struct (s3, (c, s |> Array.append [| s3 |])))

                                    struct (s2, (c, s |> Array.append [| s2 |])))

                            struct (s1, (c, s |> Array.append [| s1 |])))

                    struct (c, s))

            let out = List<_>()
            let l = c |> listenStrongC out.Add
            s[2] |> sendC 5
            s[3] |> sendC 5

            runT (fun () ->
                s[1] |> sendC 5
                s[4] |> sendC 5)

            l |> unlistenL
            do! Expect.Sequence([ 5; 9; 13; 21 ], out)
        }

    [<Test>]
    member _.``Test Lift List Large``() =
        task {
            let cellSinks = List.init 500 (fun _ -> sinkC 1)
            let sum = cellSinks |> liftAllC Seq.sum
            let out = List<_>()
            let l = sum |> listenStrongC out.Add
            cellSinks[4] |> sendC 5
            cellSinks[5] |> sendC 5

            runT (fun () ->
                cellSinks[9] |> sendC 5
                cellSinks[17] |> sendC 5
                cellSinks[41] |> sendC 5
                cellSinks[48] |> sendC 5)

            l |> unlistenL
            do! Expect.Sequence([ 500; 504; 508; 524 ], out)
        }

    [<Test>]
    member _.``Test Lift List Large Many Updates``() =
        task {
            let cellSinks = List.init 500 (fun _ -> sinkC 1)
            let sum = cellSinks |> liftAllC Seq.sum
            let out = List<_>()
            let l = sum |> listenStrongC out.Add

            for i = 0 to 99 do
                cellSinks[i * 5] |> sendC 5
                cellSinks[i * 5 + 1] |> sendC 5

                runT (fun () ->
                    cellSinks[i * 5 + 2] |> sendC 5
                    cellSinks[i * 5 + 3] |> sendC 5
                    cellSinks[i * 5 + 4] |> sendC 5)

            l |> unlistenL

            let expected =
                List.Cons(
                    500,
                    List.concat (List.init 100 (fun i -> [ 500 + 20 * i + 4; 500 + 20 * i + 8; 500 + 20 * i + 20 ]))
                )

            do! Expect.Sequence(expected, out)
        }

    [<Test>]
    member _.``Test Lift Changes While Listening``() =
        task {
            let cellSinks = List.init 50 (fun _ -> sinkC 1)
            let sum = cellSinks |> liftAllC Seq.sum
            let out = List<_>()

            let l =
                runT (fun () ->
                    cellSinks[4] |> sendC 5
                    let l = sum |> listenStrongC out.Add
                    cellSinks[5] |> sendC 5
                    l)

            cellSinks[9] |> sendC 5

            runT (fun () ->
                cellSinks[17] |> sendC 5
                cellSinks[41] |> sendC 5
                cellSinks[48] |> sendC 5)

            l.Unlisten()
            do! Expect.Sequence([ 58; 62; 74 ], out)
        }

    [<Test>]
    member _.``SwitchC On CellLoop``() =
        task {
            let struct (_, (c, c1, c2, s)) =
                loopC (fun cl ->
                    let c1 = sinkC 1
                    let c2 = sinkC 11
                    let c = cl |> switchC
                    let s = sinkC (c1 :> Cell<_>)
                    struct (s, (c, c1, c2, s)))

            let out = List<_>()
            let l = c |> listenStrongC out.Add
            c1 |> sendC 2
            c2 |> sendC 12

            runT (fun () ->
                c1 |> sendC 3
                c2 |> sendC 13
                s |> sendC (upcast c2))

            c1 |> sendC 4
            c2 |> sendC 14
            l |> unlistenL
            do! Expect.Sequence([ 1; 2; 13; 14 ], out)
        }

    [<Test>]
    member _.``SwitchS On BehaviorLoop``() =
        task {
            let struct (_, (b, b1, b2, s)) =
                loopB (fun bl ->
                    let b1 = sinkS<int> ()
                    let b2 = sinkS<int> ()
                    let b = bl |> switchSB
                    let s = sinkB (b1 :> Stream<_>)
                    struct (s, (b, b1, b2, s)))

            let out = List<_>()
            let l = b |> listenStrongS out.Add
            b1 |> sendS 2
            b2 |> sendS 12

            runT (fun () ->
                b1 |> sendS 3
                b2 |> sendS 13
                s |> sendB (upcast b2))

            b1 |> sendS 4
            b2 |> sendS 14
            l |> unlistenL
            do! Expect.Sequence([ 2; 3; 14 ], out)
        }

    [<Test>]
    member _.``SwitchC Catch First``() =
        task {
            let out = List<_>()

            let c1, c2, s, l =
                runT (fun () ->
                    let c1 = sinkC 1
                    let c2 = sinkC 11
                    let s = sinkB (c1 :> Cell<_>)
                    let c = s |> switchCB
                    c1 |> sendC 2
                    c2 |> sendC 12
                    s |> sendB (upcast c2)
                    let l = c |> listenStrongC out.Add
                    (c1, c2, s, l))

            c1 |> sendC 3
            c2 |> sendC 13

            runT (fun () ->
                c1 |> sendC 4
                c2 |> sendC 14
                s |> sendB (upcast c1))

            c1 |> sendC 5
            c2 |> sendC 15
            l |> unlistenL
            do! Expect.Sequence([ 12; 13; 4; 5 ], out)
        }

    [<Test>]
    member _.``SwitchS Catch First``() =
        task {
            let out = List<_>()

            let c1, c2, s, l =
                runT (fun () ->
                    let c1 = sinkS<int> ()
                    let c2 = sinkS<int> ()
                    let s = sinkB (c1 :> Stream<_>)
                    let c = s |> switchSB
                    c1 |> sendS 2
                    c2 |> sendS 12
                    s |> sendB (upcast c2)
                    let l = c |> listenStrongS out.Add
                    (c1, c2, s, l))

            c1 |> sendS 3
            c2 |> sendS 13

            runT (fun () ->
                c1 |> sendS 4
                c2 |> sendS 14
                s |> sendB (upcast c1))

            c1 |> sendS 5
            c2 |> sendS 15
            l |> unlistenL
            do! Expect.Sequence([ 2; 13; 14; 5 ], out)
        }

    [<Test>]
    member _.``SwitchS Catch First Before``() =
        task {
            let out = List<_>()

            let c1, c2, s, l =
                runT (fun () ->
                    let c1 = sinkS<int> ()
                    let c2 = sinkS<int> ()
                    let s = sinkB (c1 :> Stream<_>)
                    c1 |> sendS 2
                    c2 |> sendS 12
                    s |> sendB (upcast c2)
                    let c = s |> switchSB
                    let l = c |> listenStrongS out.Add
                    (c1, c2, s, l))

            c1 |> sendS 3
            c2 |> sendS 13

            runT (fun () ->
                c1 |> sendS 4
                c2 |> sendS 14
                s |> sendB (upcast c1))

            c1 |> sendS 5
            c2 |> sendS 15
            l |> unlistenL
            do! Expect.Sequence([ 2; 13; 14; 5 ], out)
        }

    [<Test>]
    member _.``Test Lift In SwitchC``() =
        task {
            let list1 = [| Test(0); Test(1); Test(2); Test(3); Test(4) |]
            let list2 = [| Test(5); Test(6); Test(7); Test(8); Test(9) |]
            let v = sinkB list1
            let c = v |> mapB ((Seq.map (fun o -> o.Value)) >> liftAllB id) |> switchBB
            let streamOutput = List<_>()
            let l = c |> Operational.updates |> listenStrongS streamOutput.Add
            let cellOutput = List<_>()
            let l2 = runT (fun () -> c |> Operational.value |> listenStrongS cellOutput.Add)
            list1[2].Value |> sendB 12
            list2[1].Value |> sendB 16
            list1[4].Value |> sendB 14

            runT (fun () ->
                list2[2].Value |> sendB 17
                list1[0].Value |> sendB 10
                v |> sendB list2)

            list1[3].Value |> sendB 13
            list2[3].Value |> sendB 18
            l2 |> unlistenL
            l |> unlistenL
            do! Expect.Equal(4, streamOutput.Count)
            do! Expect.Equal(5, cellOutput.Count)
            do! Expect.Sequence([ 0; 1; 2; 3; 4 ], cellOutput[0])
            do! Expect.Sequence([ 0; 1; 12; 3; 4 ], streamOutput[0])
            do! Expect.Sequence([ 0; 1; 12; 3; 4 ], cellOutput[1])
            do! Expect.Sequence([ 0; 1; 12; 3; 14 ], streamOutput[1])
            do! Expect.Sequence([ 0; 1; 12; 3; 14 ], cellOutput[2])
            do! Expect.Sequence([ 5; 16; 17; 8; 9 ], streamOutput[2])
            do! Expect.Sequence([ 5; 16; 17; 8; 9 ], cellOutput[3])
            do! Expect.Sequence([ 5; 16; 17; 18; 9 ], streamOutput[3])
            do! Expect.Sequence([ 5; 16; 17; 18; 9 ], cellOutput[4])
        }

    [<Test>]
    member _.``Test Map With SwitchC``() =
        task {
            let list1 = [| Test(0); Test(1); Test(2); Test(3); Test(4) |]
            let list2 = [| Test(5); Test(6); Test(7); Test(8); Test(9) |]
            let v = sinkB list1

            let c =
                v |> mapB ((Seq.map (fun o -> o.Value)) >> liftAllB id) |> mapB id |> switchBB

            let streamOutput = List<_>()
            let l = c |> Operational.updates |> listenStrongS streamOutput.Add
            let cellOutput = List<_>()
            let l2 = runT (fun () -> c |> Operational.value |> listenStrongS cellOutput.Add)
            list1[2].Value |> sendB 12
            list2[1].Value |> sendB 16
            list1[4].Value |> sendB 14

            runT (fun () ->
                list2[2].Value |> sendB 17
                list1[0].Value |> sendB 10
                v |> sendB list2)

            list1[3].Value |> sendB 13
            list2[3].Value |> sendB 18
            l2 |> unlistenL
            l |> unlistenL
            do! Expect.Equal(4, streamOutput.Count)
            do! Expect.Equal(5, cellOutput.Count)
            do! Expect.Sequence([ 0; 1; 2; 3; 4 ], cellOutput[0])
            do! Expect.Sequence([ 0; 1; 12; 3; 4 ], streamOutput[0])
            do! Expect.Sequence([ 0; 1; 12; 3; 4 ], cellOutput[1])
            do! Expect.Sequence([ 0; 1; 12; 3; 14 ], streamOutput[1])
            do! Expect.Sequence([ 0; 1; 12; 3; 14 ], cellOutput[2])
            do! Expect.Sequence([ 5; 16; 17; 8; 9 ], streamOutput[2])
            do! Expect.Sequence([ 5; 16; 17; 8; 9 ], cellOutput[3])
            do! Expect.Sequence([ 5; 16; 17; 18; 9 ], streamOutput[3])
            do! Expect.Sequence([ 5; 16; 17; 18; 9 ], cellOutput[4])
        }
