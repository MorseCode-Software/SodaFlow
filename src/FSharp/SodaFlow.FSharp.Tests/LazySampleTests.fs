module SodaFlow.Tests.LazySample

open SodaFlow
open TUnit.Core

type ``Lazy Sample Tests``() =

    /// Only SodaFlow reads a sample, thus the test reads each one through a constant cell.
    let read sample = sample |> constantLazyC |> sampleC

    /// Each lift takes its samples as one tuple, as Cell.lift2 takes its cells. Each value is one
    /// digit of the result, thus a sample in the wrong position gives a different number.
    [<Test>]
    member _.``lift2 to lift8 combine two to eight samples in their order``() =
        task {
            let one = lazySample (fun () -> 1)
            let two = constantC 2 |> sampleLazyC
            let three = lazySample (fun () -> 3)
            let four = constantC 4 |> sampleLazyC
            let five = lazySample (fun () -> 5)
            let six = constantC 6 |> sampleLazyC
            let seven = lazySample (fun () -> 7)
            let eight = constantC 8 |> sampleLazyC
            let digits values = values |> List.fold (fun total value -> total * 10 + value) 0

            do! Expect.Equal(12, read (LazySample.lift2 (fun a b -> digits [ a; b ]) (one, two)))
            do! Expect.Equal(123, read (LazySample.lift3 (fun a b c -> digits [ a; b; c ]) (one, two, three)))

            do! Expect.Equal(
                    1234,
                    read (LazySample.lift4 (fun a b c d -> digits [ a; b; c; d ]) (one, two, three, four))
                )

            do! Expect.Equal(
                    12345,
                    read (LazySample.lift5 (fun a b c d e -> digits [ a; b; c; d; e ]) (one, two, three, four, five))
                )

            do! Expect.Equal(
                    123456,
                    read (
                        LazySample.lift6
                            (fun a b c d e f -> digits [ a; b; c; d; e; f ])
                            (one, two, three, four, five, six)
                    )
                )

            do! Expect.Equal(
                    1234567,
                    read (
                        LazySample.lift7
                            (fun a b c d e f g -> digits [ a; b; c; d; e; f; g ])
                            (one, two, three, four, five, six, seven)
                    )
                )

            do! Expect.Equal(
                    12345678,
                    read (
                        LazySample.lift8
                            (fun a b c d e f g h -> digits [ a; b; c; d; e; f; g; h ])
                            (one, two, three, four, five, six, seven, eight)
                    )
                )
        }
