module SodaFlow.Tests.``Denotational Semantics``

open System.Collections.Generic
open System.Runtime.ExceptionServices
open System.Threading.Tasks
open SodaFlow
open TUnit.Core

type ``Denotational Semantics Tests``() =

    let runSimulationWithSimultaneousFirings listenStrong firings =
        let maxKey =
            match firings with
            | [] -> -1
            | _ -> firings |> Seq.collect (Map.toSeq >> Seq.map fst) |> Seq.max

        let out = List<_>()

        let run t =
            for a in
                firings
                |> Seq.collect (fun f ->
                    match f |> Map.tryFind t with
                    | None -> []
                    | Some t -> t) do
                a ()

        match maxKey with
        | -1 ->
            use _l = listenStrong out.Add
            ()
        | _ ->
            use _l =
                runT (fun () ->
                    let l = listenStrong out.Add
                    run 0
                    l)

            for i = 1 to maxKey do
                runT (fun () -> run i)

        out

    let runSimulationWithNoFirings listenStrong =
        runSimulationWithSimultaneousFirings listenStrong List.empty

    let runSimulation listenStrong firings =
        runSimulationWithSimultaneousFirings listenStrong (firings |> List.map (Map.map (fun _ a -> [ a ])))

    let mkStream firings =
        let s = sinkS ()
        let f = firings |> Map.map (fun _ v -> (fun () -> s |> sendS v))

        match f |> Map.tryFindKey (fun k _ -> k < 0) with
        | None -> ()
        | Some _ -> invalidOp "All firings must occur at T >= 0."

        (s :> 'a Stream, f)

    let mkStreamWithCoalesce firings coalesce =
        let s = sinkWithCoalesceS coalesce

        let f =
            firings
            |> List.groupBy fst
            |> Map.ofList
            |> Map.map (fun _ v -> v |> (List.map (fun v -> (fun () -> s |> sendS (snd v)))))

        match f |> Map.tryFindKey (fun k _ -> k < 0) with
        | None -> ()
        | Some _ -> invalidOp "All firings must occur at T >= 0."

        (s :> 'a Stream, f)

    let rec getPermutationsInternal list length =
        if length = 1 then
            list |> List.map (fun o -> [ o ])
        else
            getPermutationsInternal list (length - 1)
            |> List.collect (fun t ->
                list
                |> List.filter (fun e -> t |> List.contains e |> not)
                |> List.map (fun t2 -> List.append t [ t2 ]))

    let getPermutations list =
        getPermutationsInternal list list.Length

    let runPermutations
        (createListAndListener: unit -> (string * Map<int, unit -> unit>) list * (('T -> unit) -> #IListener))
        (``assert``: List<'T> -> Task)
        =
        task {
            let indexes = List.init (createListAndListener () |> fst).Length id

            for list, listener in
                (getPermutations indexes
                 |> List.map (fun ii ->
                     let list, listener = createListAndListener ()
                     (ii |> List.map (fun i -> list[i]), listener))) do
                try
                    let out = runSimulation listener (list |> List.map snd)
                    do! ``assert`` out
                with e ->
                    printfn "Test failed for ordering { %s }." (list |> List.map fst |> String.concat ", ")
                    ExceptionDispatchInfo.Capture(e).Throw()
        }

    [<Test>]
    member _.``Never: Test Case``() =
        task {
            let out = runSimulationWithNoFirings (flip listenStrongS (neverS<int> ()))
            do! Expect.Sequence([], out)
        }

    [<Test>]
    member _.``MapS: Test Case``() =
        task {
            let s, sf = mkStream ([ (0, 5); (1, 10); (2, 12) ] |> Map.ofList)
            let out = runSimulation (flip listenStrongS (s |> mapS ((+) 1))) [ sf ]
            do! Expect.Sequence([ 6; 11; 13 ], out)
        }

    [<Test>]
    member _.``Snapshot: Test Case``() =
        task {
            let s1, s1f = mkStream ([ (0, 'a'); (3, 'b'); (5, 'c') ] |> Map.ofList)
            let s2, s2f = mkStream ([ (1, 4); (5, 7) ] |> Map.ofList)
            let c = s2 |> holdS 3
            let out = runSimulation (flip listenStrongS (s1 |> snapshotAndTakeC c)) [ s1f; s2f ]
            do! Expect.Sequence([ 3; 4; 4 ], out)
        }

    [<Test>]
    member _.``Merge: Test Case``() =
        task {
            let s1, s1f = mkStream ([ (0, 0); (2, 2) ] |> Map.ofList)
            let s2, s2f = mkStream ([ (1, 10); (2, 20); (3, 30) ] |> Map.ofList)
            let out = runSimulation (flip listenStrongS ((s1, s2) |> mergeS (+))) [ s1f; s2f ]
            do! Expect.Sequence([ 0; 10; 22; 30 ], out)
        }

    [<Test>]
    member _.``Filter: Test Case``() =
        task {
            let s, sf = mkStream ([ (0, 5); (1, 6); (2, 7) ] |> Map.ofList)

            let out =
                runSimulation (flip listenStrongS (s |> filterS ((flip (%) 2) >> ((<>) 0)))) [ sf ]

            do! Expect.Sequence([ 5; 7 ], out)
        }

    [<Test>]
    member _.``SwitchS: Test Case``() =
        task {
            do!
                runPermutations
                    (fun () ->
                        let s1, s1f = mkStream ([ (0, 'a'); (1, 'b'); (2, 'c'); (3, 'd') ] |> Map.ofList)
                        let s2, s2f = mkStream ([ (0, 'W'); (1, 'X'); (2, 'Y'); (3, 'Z') ] |> Map.ofList)
                        let switcher, switcherF = mkStream ([ 1, s2 ] |> Map.ofList)
                        let c = switcher |> holdS s1
                        let firings = [ ("s1", s1f); ("s2", s2f); ("switcher", switcherF) ]
                        (firings, flip listenStrongS (c |> switchS)))
                    (fun out -> Expect.Sequence([ 'a'; 'b'; 'Y'; 'Z' ], out))
        }

    [<Test>]
    member _.``Updates: Test Case``() =
        task {
            let s, sf = mkStream ([ (1, 'b'); (3, 'c') ] |> Map.ofList)
            let c = s |> holdS 'a'
            let out = runSimulation (flip listenStrongS (c |> updatesC)) [ sf ]
            do! Expect.Sequence([ 'b'; 'c' ], out)
        }

    [<Test>]
    member _.``Value: Test Case 1``() =
        task {
            let s, sf = mkStream ([ (1, 'b'); (3, 'c') ] |> Map.ofList)
            let c = s |> holdS 'a'

            let out =
                runSimulation (fun h -> runT (fun () -> c |> valuesC |> listenStrongS h)) [ sf ]

            do! Expect.Sequence([ 'a'; 'b'; 'c' ], out)
        }

    [<Test>]
    member _.``Value: Test Case 2``() =
        task {
            let s, sf = mkStream ([ (0, 'b'); (1, 'c'); (3, 'd') ] |> Map.ofList)
            let c = s |> holdS 'a'

            let out =
                runSimulation (fun h -> runT (fun () -> c |> valuesC |> listenStrongS h)) [ sf ]

            do! Expect.Sequence([ 'b'; 'c'; 'd' ], out)
        }

    [<Test>]
    member _.``ListenC: Test Case 1``() =
        task {
            let s, sf = mkStream ([ (1, 'b'); (3, 'c') ] |> Map.ofList)
            let c = s |> holdS 'a'
            let out = runSimulation (flip listenStrongC c) [ sf ]
            do! Expect.Sequence([ 'a'; 'b'; 'c' ], out)
        }

    [<Test>]
    member _.``ListenC: Test Case 2``() =
        task {
            let s, sf = mkStream ([ (0, 'b'); (1, 'c'); (3, 'd') ] |> Map.ofList)
            let c = s |> holdS 'a'
            let out = runSimulation (flip listenStrongC c) [ sf ]
            do! Expect.Sequence([ 'b'; 'c'; 'd' ], out)
        }

    [<Test>]
    member _.``Split: Test Case``() =
        task {
            let s, sf =
                mkStreamWithCoalesce [ (0, [ 'a'; 'b' ]); (1, [ 'c' ]); (1, [ 'd'; 'e' ]) ] List.append

            let out =
                runSimulationWithSimultaneousFirings (flip listenStrongS (s |> Operational.split)) [ sf ]

            do! Expect.Sequence([ 'a'; 'b'; 'c'; 'd'; 'e' ], out)
        }

    [<Test>]
    member _.``Constant: Test Case``() =
        task {
            let c = constantC 'a'
            let out = runSimulationWithNoFirings (flip listenStrongC c)
            do! Expect.Sequence([ 'a' ], out)
        }

    [<Test>]
    member _.``ConstantLazy: Test Case``() =
        task {
            let c = constantLazyC (lazy 'a')
            let out = runSimulationWithNoFirings (flip listenStrongC c)
            do! Expect.Sequence([ 'a' ], out)
        }

    [<Test>]
    member _.``Hold: Test Case``() =
        task {
            let s, sf = mkStream ([ (1, 'b'); (3, 'c') ] |> Map.ofList)
            let c = s |> holdS 'a'
            let out = runSimulation (flip listenStrongC c) [ sf ]
            do! Expect.Sequence([ 'a'; 'b'; 'c' ], out)
        }

    [<Test>]
    member _.``MapC: Test Case``() =
        task {
            let s, sf = mkStream ([ (2, 3); (3, 5) ] |> Map.ofList)
            let c = s |> holdS 0
            let out = runSimulation (flip listenStrongC (c |> mapC ((+) 1))) [ sf ]
            do! Expect.Sequence([ 1; 4; 6 ], out)
        }

    [<Test>]
    member _.``Apply: Test Case``() =
        task {
            let s1, s1f = mkStream ([ (1, 200); (2, 300); (4, 400) ] |> Map.ofList)
            let ca = s1 |> holdS 100
            let s2, s2f = mkStream ([ (1, (+) 5); (3, (+) 6) ] |> Map.ofList)
            let cf = s2 |> holdS ((+) 0)
            let out = runSimulation (flip listenStrongC (ca |> applyC cf)) [ s1f; s2f ]
            do! Expect.Sequence([ 100; 205; 305; 306; 406 ], out)
        }

    [<Test>]
    member _.``SwitchC: Test Case 1``() =
        task {
            do!
                runPermutations
                    (fun () ->
                        let s1, s1f = mkStream ([ (0, 'b'); (1, 'c'); (2, 'd'); (3, 'e') ] |> Map.ofList)
                        let c1 = s1 |> holdS 'a'
                        let s2, s2f = mkStream ([ (0, 'W'); (1, 'X'); (2, 'Y'); (3, 'Z') ] |> Map.ofList)
                        let c2 = s2 |> holdS 'V'
                        let switcher, switcherF = mkStream ([ 1, c2 ] |> Map.ofList)
                        let c = switcher |> holdS c1
                        let firings = [ ("s1", s1f); ("s2", s2f); ("switcher", switcherF) ]
                        (firings, flip listenStrongC (c |> switchC)))
                    (fun out -> Expect.Sequence([ 'b'; 'X'; 'Y'; 'Z' ], out))
        }

    [<Test>]
    member _.``SwitchC: Test Case 2``() =
        task {
            do!
                runPermutations
                    (fun () ->
                        let s1, s1f = mkStream ([ (0, 'b'); (1, 'c'); (2, 'd'); (3, 'e') ] |> Map.ofList)
                        let c1 = s1 |> holdS 'a'
                        let s2, s2f = mkStream ([ (1, 'X'); (2, 'Y'); (3, 'Z') ] |> Map.ofList)
                        let c2 = s2 |> holdS 'W'
                        let switcher, switcherF = mkStream ([ 1, c2 ] |> Map.ofList)
                        let c = switcher |> holdS c1
                        let firings = [ ("s1", s1f); ("s2", s2f); ("switcher", switcherF) ]
                        (firings, flip listenStrongC (c |> switchC)))
                    (fun out -> Expect.Sequence([ 'b'; 'X'; 'Y'; 'Z' ], out))
        }

    [<Test>]
    member _.``SwitchC: Test Case 3``() =
        task {
            do!
                runPermutations
                    (fun () ->
                        let s1, s1f = mkStream ([ (0, 'b'); (1, 'c'); (2, 'd'); (3, 'e') ] |> Map.ofList)
                        let c1 = s1 |> holdS 'a'
                        let s2, s2f = mkStream ([ (2, 'Y'); (3, 'Z') ] |> Map.ofList)
                        let c2 = s2 |> holdS 'X'
                        let switcher, switcherF = mkStream ([ 1, c2 ] |> Map.ofList)
                        let c = switcher |> holdS c1
                        let firings = [ ("s1", s1f); ("s2", s2f); ("switcher", switcherF) ]
                        (firings, flip listenStrongC (c |> switchC)))
                    (fun out -> Expect.Sequence([ 'b'; 'X'; 'Y'; 'Z' ], out))
        }

    [<Test>]
    member _.``SwitchC: Test Case 4``() =
        task {
            do!
                runPermutations
                    (fun () ->
                        let s1, s1f = mkStream ([ (0, 'b'); (1, 'c'); (2, 'd'); (3, 'e') ] |> Map.ofList)
                        let c1 = s1 |> holdS 'a'
                        let s2, s2f = mkStream ([ (0, 'W'); (1, 'X'); (2, 'Y'); (3, 'Z') ] |> Map.ofList)
                        let c2 = s2 |> holdS 'V'
                        let s3, s3f = mkStream ([ (0, '2'); (1, '3'); (2, '4'); (3, '5') ] |> Map.ofList)
                        let c3 = s3 |> holdS '1'
                        let switcher, switcherF = mkStream ([ (1, c2); (3, c3) ] |> Map.ofList)
                        let c = switcher |> holdS c1
                        let firings = [ ("s1", s1f); ("s2", s2f); ("s3", s3f); ("switcher", switcherF) ]
                        (firings, flip listenStrongC (c |> switchC)))
                    (fun out -> Expect.Sequence([ 'b'; 'X'; 'Y'; '5' ], out))
        }

    [<Test>]
    member _.``Sample: Test Case``() =
        task {
            let s = sinkS ()
            let c = s |> holdS 'a'
            let sample1 = c |> sampleC
            s |> sendS 'b'
            let sample2 = c |> sampleC
            do! Expect.Equal('a', sample1)
            do! Expect.Equal('b', sample2)
        }

    [<Test>]
    member _.``SampleLazy: Test Case``() =
        task {
            let s = sinkS ()
            let c = s |> holdS 'a'
            let sample1 = c |> sampleLazyC
            s |> sendS 'b'
            let sample2 = c |> sampleLazyC
            do! Expect.Equal('a', sample1.Value)
            do! Expect.Equal('b', sample2.Value)
        }
