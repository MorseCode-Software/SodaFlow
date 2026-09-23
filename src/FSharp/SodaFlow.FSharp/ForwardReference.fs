/// <summary>
///     Building a value which refers to itself.
/// </summary>
/// <remarks>
///     A cell loop gives a reference to a cell before the cell exists, and the cell that the
///     reference means closes the loop. This is the same, but it removes the cell. The function
///     gives one value and not a cell of changing values, and a constant cell closes the loop that
///     the function gets. Thus, the reference resolves to that value and never changes.
///
///     Use this for the cycle between two objects where each one needs the other one at its
///     construction. Without this, the code must make one object incomplete and complete it after
///     the other one.
///
///     The reference is a promise about a value, and not the value. A read of it during the
///     construction, with <c>sampleC</c> or with an operation on it, asks a question that has no
///     answer now. The read throws an exception to say this, as it does for each looped cell.
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
///     A struct tuple of the value that closed the forward reference, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     The captures are for the parts that this code makes with the value, and that the value does
///     not give. Examples are a sink that sends to it, and an internal cell to monitor. No code can
///     get these after the function returns.
///
///     The reference and its resolution occur in one transaction, which this opens when no
///     transaction is open.
///
///     Use <c>createWithNoCaptures</c> where the caller needs only the value.
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
///     Builds a value which can refer to itself, where the caller needs only the value.
/// </summary>
/// <param name="f">Given the forward reference, returns the value it stands for.</param>
/// <returns>The value that closed the forward reference.</returns>
/// <remarks>
///     <c>create</c> where the caller needs more than the value from the construction.
/// </remarks>
let createWithNoCaptures f =
    let struct (value, _) = create (fun reference -> struct (f reference, ()))
    value
