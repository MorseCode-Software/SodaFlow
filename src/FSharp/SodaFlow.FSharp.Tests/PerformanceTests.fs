module SodaFlow.Tests.Performance

open System.Collections.Generic
open NUnit.Framework
open SodaFlow

[<AutoOpen>]
module private Types =

    type TestObject(s: Stream<bool>, s1: Stream<int>, s2: Stream<int>) as this =
        let mutable currentValue = lazy 0

        let cell, l =
            runT (fun () ->
                let cell =
                    ((s |> mapS (fun v -> if v then 1 else 0), s1) |> orElseS, s2)
                    |> orElseS
                    |> holdS 0

                let createCell () =
                    (s1 |> snapshotC cell (+) |> filterS (flip (>) 5),
                     s
                     |> snapshotAndTakeC ((s1 |> holdS 0, s2 |> holdS 1) |> lift2C (+))
                     |> mapS ((+) 1))
                    |> orElseS
                    |> holdS 3

                let _cell2 = createCell ()
                let _cell3 = createCell ()
                let _cell4 = createCell ()
                let _cell5 = createCell ()
                let _cell6 = createCell ()
                let _cell7 = createCell ()
                let _cell8 = createCell ()
                let _cell9 = createCell ()
                currentValue <- cell |> sampleLazyC
                let l = cell |> updatesC |> listenStrongS (fun v -> this.CurrentValue <- v)
                (cell, l))

        member private _.L = l
        member _.Cell = cell

        member _.CurrentValue
            with get () = currentValue.Value
            and set value = currentValue <- lazy value

    type TestObject2(id: int, initialIsSelected: bool, selectAllStream: Stream<bool>) =
        let isSelectedStreamSink = sinkS ()

        let isSelected =
            (selectAllStream, isSelectedStreamSink) |> orElseS |> holdS initialIsSelected

        member _.Id = id
        member _.IsSelectedStreamSink = isSelectedStreamSink
        member _.IsSelected = isSelected

[<TestFixture>]
type ``Performance Tests``() =

    [<Test>]
    member _.``Test Merge``() =
        let s = sinkS<unit> ()

        let struct (_, obj) =
            loopS (fun loop ->
                let s1 = sinkCS ()
                let s2 = sinkCS ()
                let l = Array.init 5000 (fun _ -> TestObject(loop, s1, s2))

                struct (s
                        |> snapshotAndTakeC (l |> Seq.map (fun o -> o.Cell) |> liftAllC id)
                        |> mapS (Seq.forall (fun v -> v = 0)),
                        l))

        let values = obj |> Array.map (fun o -> o.CurrentValue)
        CollectionAssert.AreEqual(Seq.init 5000 (fun _ -> 0), values)

    [<Test>]
    member _.``Test Run Construct``() =
        let objects =
            runT (fun () ->
                let o2 = List.init 10000 (fun n -> TestObject2(n, n < 1500, neverS ()))
                sinkC o2)

        runT (fun () -> objects |> sendC (List.init 20000 (fun n -> TestObject2(n, n < 500, neverS ()))))

    [<Test>]
    member _.``Test Run Construct 2``() =
        let struct (_, (objectsAndIsSelected, selectAllStream, objects)) =
            loopC (fun allSelected ->
                let toggleAllSelectedStream = sinkS ()

                let selectAllStream =
                    toggleAllSelectedStream
                    |> snapshotAndTakeC allSelected
                    |> mapS (fun a ->
                        match a with
                        | Some a -> not a
                        | None -> true)

                let o2 = List.init 10000 (fun n -> TestObject2(n, n < 1500, selectAllStream))
                let objects = sinkC o2

                let objectsAndIsSelected =
                    objects
                    |> mapC (Seq.map (fun o -> o.IsSelected |> mapC (fun s -> (o, s))) >> liftAllC id)
                    |> switchC

                let defaultValue = Some(o2.Length < 1)

                let allSelected =
                    objectsAndIsSelected
                    |> mapC (fun oo ->
                        if oo.Count > 0 then
                            (if oo |> Seq.forall snd then
                                 Some true
                             else
                                 (if oo |> Seq.forall (fun (_, isSelected) -> not isSelected) then
                                      Some false
                                  else
                                      None))
                        else
                            defaultValue)

                struct (allSelected, (objectsAndIsSelected, selectAllStream, objects)))

        let out = List<_>()

        (use _l =
            runT (fun () ->
                objectsAndIsSelected
                |> mapC (Seq.where snd >> Seq.length)
                |> listenStrongC out.Add)

         runT (fun () ->
             objects
             |> sendC (List.init 20000 (fun n -> TestObject2(n, n < 500, selectAllStream)))))

        CollectionAssert.AreEqual([ 1500; 500 ], out)
