namespace SodaFlow.Tests

[<AutoOpen>]
module internal Utils =
    open TUnit.Assertions.Exceptions

    let inline flip f x y = f y x

    let internal assertExists notExistsMessage onExists =
        function
        | Some o -> onExists o
        | None -> raise (AssertionException notExistsMessage)

    let internal assertExceptionExists onExists (e: #exn option) =
        assertExists "No exception was encountered." onExists e

module internal Lazy =
    let inline internal map f (l: Lazy<_>) = lazy (f l.Value)

module internal Async =
    open System.Threading.Tasks

    let internal StartAsVoidTask (a: Async<unit>) : Task = upcast (a |> Async.StartAsTask)
