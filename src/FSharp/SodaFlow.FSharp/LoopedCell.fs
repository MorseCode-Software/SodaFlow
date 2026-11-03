/// <summary>
///     Operations on a cell that can be in a loop that is not closed.
/// </summary>
/// <remarks>
///     A <c>LoopedCell</c> has each operation of a cell except a read of the current value. A
///     loop block gives one as the placeholder. Each operation here starts its result
///     from the current value of its input, which is not available before the loop closes, thus the
///     result is a looped cell too. The functions in <c>Cell</c> take a <c>Cell</c>, which is a
///     looped cell that has a value now. The operations that give a stream are in <c>Cell</c> and
///     <c>Stream</c>, and they take a looped cell.
///
///     <c>close</c> gives the cell back when the loop is closed, with a check. <c>asCell</c>
///     asserts it before the close.
/// </remarks>
module SodaFlow.LoopedCell

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

/// <summary>
///     Asserts that no code reads the looped cell before its loop closes, and gives it as a cell.
/// </summary>
/// <param name="cell">The looped cell.</param>
/// <returns>The same value as a cell.</returns>
/// <remarks>
///     Use this to give a placeholder to code that keeps the cell and reads it after the close. The types
///     cannot see that the read comes after the close, thus this call is the assertion. A read
///     before the close throws an exception. Where the loop is closed, use <c>close</c>, which
///     checks it.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let asCell (cell: LoopedCell<_>) = cell.AsCellImpl()

/// <summary>
///     Gives the looped cell as a cell, after a check that each loop it depends on is closed.
/// </summary>
/// <param name="cell">The looped cell.</param>
/// <returns>The same value as a cell.</returns>
/// <exception cref="InvalidOperationException">A loop is not closed.</exception>
/// <remarks>
///     A loop block returns a looped cell in its captures, and the type stays looped after
///     the block returns and the loop closes. This gives the cell back, with a check and not the
///     assertion that <c>asCell</c> makes.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let close (cell: LoopedCell<_>) = cell.CloseImpl()

/// <summary>
///     Transforms a looped cell with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="cell">The looped cell to transform.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the input's value.</returns>
/// <remarks>
///     See <c>Cell.map</c> for the contract of <paramref name="f" />.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (cell: LoopedCell<_>) : LoopedCell<_> =
    cell.MapImpl(Func<_, _> f) :> LoopedCell<_>

/// <summary>
///     Applies a looped cell of functions to a looped cell of values.
/// </summary>
/// <param name="f">The looped cell holding the function to apply.</param>
/// <param name="cell">The looped cell holding the value to apply it to.</param>
/// <returns>
///     A looped cell whose value is the result of the current function in <paramref name="f" /> on
///     the input's value.
/// </returns>
/// <remarks>
///     See <c>Cell.apply</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let apply f (cell: LoopedCell<_>) : LoopedCell<_> =
    cell.ApplyImpl(f |> map (fun f -> Func<_, _> f)) :> LoopedCell<_>

/// <summary>
///     Combines two cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the two inputs.</returns>
/// <remarks>
///     See <c>Cell.lift2</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 f (cell: LoopedCell<_>, cell2: LoopedCell<_>) : LoopedCell<_> =
    cell.LiftImpl(cell2, Func<_, _, _> f) :> LoopedCell<_>

/// <summary>
///     Combines three cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the three inputs.</returns>
/// <remarks>
///     See <c>Cell.lift3</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3 f (cell: LoopedCell<_>, cell2: LoopedCell<_>, cell3: LoopedCell<_>) : LoopedCell<_> =
    cell.LiftImpl(cell2, cell3, Func<_, _, _, _> f) :> LoopedCell<_>

/// <summary>
///     Combines four cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the four inputs.</returns>
/// <remarks>
///     See <c>Cell.lift4</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4
    f
    (cell: LoopedCell<_>, cell2: LoopedCell<_>, cell3: LoopedCell<_>, cell4: LoopedCell<_>)
    : LoopedCell<_> =
    cell.LiftImpl(cell2, cell3, cell4, Func<_, _, _, _, _> f) :> LoopedCell<_>

/// <summary>
///     Combines five cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the five current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the five inputs.</returns>
/// <remarks>
///     See <c>Cell.lift5</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5
    f
    (cell: LoopedCell<_>, cell2: LoopedCell<_>, cell3: LoopedCell<_>, cell4: LoopedCell<_>, cell5: LoopedCell<_>)
    : LoopedCell<_> =
    cell.LiftImpl(cell2, cell3, cell4, cell5, Func<_, _, _, _, _, _> f) :> LoopedCell<_>

/// <summary>
///     Combines six cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the six current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the six inputs.</returns>
/// <remarks>
///     See <c>Cell.lift6</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6
    f
    (cell: LoopedCell<_>,
     cell2: LoopedCell<_>,
     cell3: LoopedCell<_>,
     cell4: LoopedCell<_>,
     cell5: LoopedCell<_>,
     cell6: LoopedCell<_>)
    : LoopedCell<_> =
    cell.LiftImpl(cell2, cell3, cell4, cell5, cell6, Func<_, _, _, _, _, _, _> f) :> LoopedCell<_>

/// <summary>
///     Combines seven cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the seven current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <param name="cell7">The seventh cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the seven inputs.</returns>
/// <remarks>
///     See <c>Cell.lift7</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift7
    f
    (cell: LoopedCell<_>,
     cell2: LoopedCell<_>,
     cell3: LoopedCell<_>,
     cell4: LoopedCell<_>,
     cell5: LoopedCell<_>,
     cell6: LoopedCell<_>,
     cell7: LoopedCell<_>)
    : LoopedCell<_> =
    cell.LiftImpl(cell2, cell3, cell4, cell5, cell6, cell7, Func<_, _, _, _, _, _, _, _> f) :> LoopedCell<_>

/// <summary>
///     Combines eight cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the eight current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <param name="cell7">The seventh cell.</param>
/// <param name="cell8">The eighth cell.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on the eight inputs.</returns>
/// <remarks>
///     See <c>Cell.lift8</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift8
    f
    (cell: LoopedCell<_>,
     cell2: LoopedCell<_>,
     cell3: LoopedCell<_>,
     cell4: LoopedCell<_>,
     cell5: LoopedCell<_>,
     cell6: LoopedCell<_>,
     cell7: LoopedCell<_>,
     cell8: LoopedCell<_>)
    : LoopedCell<_> =
    cell.LiftImpl(cell2, cell3, cell4, cell5, cell6, cell7, cell8, Func<_, _, _, _, _, _, _, _, _> f)
    :> LoopedCell<_>

/// <summary>
///     Suppresses updates whose value the given function considers equal to the current value.
/// </summary>
/// <param name="compare">Gives true when this code must read two values as equal.</param>
/// <param name="cell">The looped cell to calm.</param>
/// <returns>A looped cell that updates only when the value changed.</returns>
/// <remarks>
///     See <c>Cell.calmWithCompare</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithCompare compare (cell: LoopedCell<_>) : LoopedCell<_> =
    cell.CalmImpl(Func<_, _, _> compare) :> LoopedCell<_>

/// <summary>
///     Suppresses updates whose value the given comparer considers equal to the current value.
/// </summary>
/// <param name="equalityComparer">Gives true when two values are equal.</param>
/// <param name="cell">The looped cell to calm.</param>
/// <returns>A looped cell that updates only when the value changed.</returns>
/// <remarks>
///     See <c>Cell.calmWithEqualityComparer</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithEqualityComparer (equalityComparer: IEqualityComparer<_>) (cell: LoopedCell<_>) : LoopedCell<_> =
    cell.CalmImpl(Func<_, _, _>(fun x y -> equalityComparer.Equals(x, y))) :> LoopedCell<_>

/// <summary>
///     Suppresses updates equal, by F# structural equality, to the current value.
/// </summary>
/// <param name="cell">The looped cell to calm.</param>
/// <returns>A looped cell that updates only when the value changed.</returns>
/// <remarks>
///     See <c>Cell.calm</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calm (cell: LoopedCell<_>) : LoopedCell<_> =
    cell.CalmImpl(Func<_, _, _> (=)) :> LoopedCell<_>

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllCollection f (cells: IReadOnlyCollection<'Cell>) =
    CellExtensionMethodsInternal.LiftCellsImpl(cells, (Func<_, _> f))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllSeq f (cells: seq<'Cell>) =
    CellExtensionMethodsInternal.LiftCellsImpl(cells, (Func<_, _> f))

/// <summary>
///     Combines any number of cells, of which one or more is looped, into one looped cell.
/// </summary>
/// <param name="f">Combines the current values, in the sequence of the cells.</param>
/// <param name="cells">The cells to put together.</param>
/// <returns>A looped cell whose value is the result of <paramref name="f" /> on all the inputs.</returns>
/// <remarks>
///     See <c>Cell.liftAll</c>.
/// </remarks>
let liftAll f (cells: seq<'Cell>) : LoopedCell<_> =
    match cells with
    | :? IReadOnlyCollection<'Cell> as cells -> liftAllCollection f cells :> LoopedCell<_>
    | cells -> liftAllSeq f cells :> LoopedCell<_>

/// <summary>
///     Unwraps a looped cell of behaviors into a looped behavior which follows the current one.
/// </summary>
/// <param name="cell">The looped cell holding a behavior.</param>
/// <returns>A looped behavior whose value is the current value of the current behavior.</returns>
/// <remarks>
///     See <c>Cell.switchB</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchB (cell: LoopedCell<_>) : LoopedBehavior<_> =
    CellExtensionMethodsInternal.SwitchBImpl cell :> LoopedBehavior<_>

/// <summary>
///     Unwraps a looped cell of cells into a looped cell which follows the current one.
/// </summary>
/// <param name="cell">The looped cell that holds a second cell.</param>
/// <returns>A looped cell whose value is the current value of the current cell.</returns>
/// <remarks>
///     See <c>Cell.switchC</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchC (cell: LoopedCell<_>) : LoopedCell<_> =
    CellExtensionMethodsInternal.SwitchCImpl cell :> LoopedCell<_>
