module SodaFlow.Tests.LazySample

open SodaFlow
open TUnit.Core

type ``Lazy Sample Tests``() =

    /// Only SodaFlow reads a sample, thus the test reads each one through a constant cell.
    let read sample = sample |> constantLazyC |> sampleC

    [<Test>]
    member _.``lift2 to lift8 combine two to eight samples``() =
        task {
            let one = lazySample (fun () -> 1)
            let two = constantC 2 |> sampleLazyC
            let three = lazySample (fun () -> 3)
            let four = constantC 4 |> sampleLazyC
            let five = lazySample (fun () -> 5)
            let six = constantC 6 |> sampleLazyC
            let seven = lazySample (fun () -> 7)
            let eight = constantC 8 |> sampleLazyC

            do! Expect.Equal(3, read (LazySample.lift2 (fun a b -> a + b) one two))
            do! Expect.Equal(6, read (LazySample.lift3 (fun a b c -> a + b + c) one two three))
            do! Expect.Equal(10, read (LazySample.lift4 (fun a b c d -> a + b + c + d) one two three four))

            do! Expect.Equal(15, read (LazySample.lift5 (fun a b c d e -> a + b + c + d + e) one two three four five))

            do! Expect.Equal(
                    21,
                    read (LazySample.lift6 (fun a b c d e f -> a + b + c + d + e + f) one two three four five six)
                )

            do! Expect.Equal(
                    28,
                    read (
                        LazySample.lift7
                            (fun a b c d e f g -> a + b + c + d + e + f + g)
                            one
                            two
                            three
                            four
                            five
                            six
                            seven
                    )
                )

            do! Expect.Equal(
                    36,
                    read (
                        LazySample.lift8
                            (fun a b c d e f g h -> a + b + c + d + e + f + g + h)
                            one
                            two
                            three
                            four
                            five
                            six
                            seven
                            eight
                    )
                )
        }
