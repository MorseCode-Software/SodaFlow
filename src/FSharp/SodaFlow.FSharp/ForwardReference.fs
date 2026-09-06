/// <summary>
///     Building a value which refers to itself.
/// </summary>
/// <remarks>
///     A cell loop lets a cell be referred to before it exists, and is closed with the cell the
///     reference turned out to mean. This is that with the cell taken back out: the function
///     produces a single value rather than a cell of changing ones, and the loop it is handed is
///     closed with a constant cell, so the reference resolves to that value and never changes.
///
///     What it is for is the knot two objects tie when each needs the other at construction, which
///     otherwise forces one of them to be built half-formed and completed afterward.
///
///     The reference is a promise about what a value will be, not the value. Reading it during
///     construction - with <c>sampleC</c> or anything built on it - asks a question which has no
///     answer yet, and says so by throwing, exactly as it does for any looped cell.
/// </remarks>
module SodaFlow.ForwardReference

open System.Runtime.CompilerServices

/// <summary>
///     Builds a value which can refer to itself, along with anything else worth keeping from its
///     construction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the value it stands for and anything
///     else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the value the forward reference was closed with, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     The captures are for the parts built along the way which the value itself does not expose -
///     a sink to feed it, an inner cell to observe - and which would otherwise be unreachable once
///     the function has returned.
///
///     Both the reference and its resolution happen in a single transaction, which this opens if
///     none is running.
///
///     Use <c>createWithNoCaptures</c> where nothing but the value itself is needed.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create f =
    let struct (_, result) =
        Cell.loop (fun reference ->
            let result = f reference
            let struct (value, _) = result
            struct (Cell.constant value, result))

    result

/// <summary>
///     Builds a value which can refer to itself, where nothing but the value itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the value it stands for.</param>
/// <returns>The value the forward reference was closed with.</returns>
/// <remarks>
///     <c>create</c> where something more than the value needs to escape the construction.
/// </remarks>
let createWithNoCaptures f =
    let struct (value, _) = create (fun reference -> struct (f reference, ()))
    value
