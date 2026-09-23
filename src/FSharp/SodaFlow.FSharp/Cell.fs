/// <summary>
///     Creating cells and combining them.
/// </summary>
/// <remarks>
///     A cell is a value that varies over time and also exposes the stream of its own changes. A
///     cell can do all that a behavior can do. A cell adds <c>updates</c> and <c>values</c>, and
///     the operations that use them. Where the changes are not necessary, <c>Behavior</c> has a
///     lower cost.
///
///     Build the graph in <c>Transaction.run</c> so the graph keeps the first firing. This
///     matters most with <c>values</c>, which always fires immediately: a listener attached in a
///     subsequent transaction did not get that firing.
/// </remarks>
module SodaFlow.Cell

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

/// <summary>
///     Creates a cell with a value that never changes.
/// </summary>
/// <param name="value">The value the cell always has.</param>
/// <returns>A cell whose value is always <paramref name="value" />.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let constant value = CellInternal.ConstantImpl value

/// <summary>
///     Creates a cell with a value that never changes, computed on first use.
/// </summary>
/// <param name="value">The lazy value the cell always has.</param>
/// <returns>A cell whose value is always the value of <paramref name="value" />.</returns>
/// <remarks>
///     For a constant with a high cost, or that is not available when the code builds the graph.
///     The code forces the value only at the first sample of the cell.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let constantLazy value = CellInternal.ConstantLazyImpl value

/// <summary>
///     Builds a cell which refers to itself, closing the loop in one transaction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the cell it stands for and
///     anything else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the cell that closed the forward reference, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     A cell that refers to itself needs a forward reference. The code makes the reference before
///     the value that it refers to. The reference and its resolution must occur in one transaction,
///     which this opens when no transaction is open.
///
///     Use <c>loopWithNoCaptures</c> where the caller needs only the cell.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let loop f =
    TransactionInternal.Apply(fun transaction _ ->
        let l = LoopedCell()
        let struct (s, r) = f l
        l.Loop(transaction, s)
        struct (s, r))

/// <summary>
///     Builds a self-referential cell where the caller needs only the cell.
/// </summary>
/// <param name="f">Given the forward reference, returns the cell it stands for.</param>
/// <returns>The cell that closed the forward reference.</returns>
/// <remarks>
///     <c>loop</c> where the caller needs more than the cell from the loop.
/// </remarks>
let loopWithNoCaptures f =
    let struct (l, _) = loop (fun s -> struct (f s, ()))
    l

/// <summary>
///     Gets a cell's current value.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>The value the cell has at this moment.</returns>
/// <remarks>
///     The functions that the primitives give to a stream can call this, and there it is the same as
///     a snapshot. With no transaction it opens its own transaction, thus a read never gives a value
///     in the middle of an update.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sample (cell: Cell<_>) = cell.SampleImpl()

/// <summary>
///     Gets the current value of a cell, and does not force it.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>A lazy value which yields what the cell held at the moment of this call.</returns>
/// <remarks>
///     This code sets the value now and calculates it after this. This is necessary for the loop
///     constructs. At the moment that the code closes a loop, the value is not known, but the
///     moment of the value is known.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sampleLazy (cell: Cell<_>) = cell.SampleLazyImpl()

/// <summary>
///     Gets a stream firing the new value of a cell each time it changes.
/// </summary>
/// <param name="cell">The cell to monitor.</param>
/// <returns>A stream firing the updated value, in the transaction the update happened in.</returns>
/// <remarks>
///     Does not fire for the value the cell starts with - only for changes. Use <c>values</c> to
///     get that initial value as a firing too.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let updates (cell: Cell<_>) = cell.UpdatesImpl

/// <summary>
///     Gets a stream firing the current value of the cell immediately, and its new value on each change.
/// </summary>
/// <param name="cell">The cell to monitor.</param>
/// <returns>
///     A stream which fires the current value in the transaction of this call, and then the
///     updated value on each change.
/// </returns>
/// <remarks>
///     The first firing occurs in the transaction of this call. Thus, a caller must call this in
///     <c>Transaction.run</c> for a listener to see that firing. A listener that attaches after that,
///     in a subsequent transaction, does not get it. This is why most code puts the construction of a
///     graph in a transaction.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let values (cell: Cell<_>) = cell.ValuesImpl

/// <summary>
///     Views a cell as a behavior.
/// </summary>
/// <param name="cell">The cell to view.</param>
/// <returns>The same value, seen as a behavior, without the stream of its changes.</returns>
/// <remarks>
///     This creates nothing and changes nothing. A cell is a behavior with updates attached. Use
///     this to give a cell to code that takes a <c>Behavior</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let asBehavior (cell: Cell<_>) = cell.BehaviorImpl

/// <summary>
///     Listens for changes without keeping the cell alive.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>A weak listener. <c>WeakListener.unlisten</c> stops it.</returns>
/// <remarks>
///     The listener stops when a GC collects the cell, thus this is the correct selection where there
///     is no clear moment to stop the listener. Keep the handle from this call in a field of the
///     object doing the listening, and the two go away together. Where other code must keep the cell
///     alive for as long as something is listening, use <c>listenStrong</c>.
///
///     Fires the current value immediately, in the transaction of this call.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listen handler (cell: Cell<_>) = cell.ListenImpl(Action<_> handler)

/// <summary>
///     Listens for changes, keeping the cell alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>
///     A strong listener. <c>StrongListener.unlisten</c> stops it, and a disposal also stops it.
/// </returns>
/// <remarks>
///     The listener roots the cell, so the graph behind it stays alive for as long as the returned
///     handle is reachable. Keep the handle and stop it when finished, or use <c>listen</c>
///     where there is no good moment to do that.
///
///     Fires the current value immediately, in the transaction of this call. The handler runs
///     with the transaction lock held, thus it must return quickly.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenStrong handler (cell: Cell<_>) =
    cell.ListenStrongImpl(Action<_> handler)

/// <summary>
///     Transforms a cell with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="cell">The cell to transform.</param>
/// <returns>A cell whose value is the result of <paramref name="f" /> on the input cell's current value.</returns>
/// <remarks>
///     <paramref name="f" /> can build FRP logic, and it can sample a behavior and a cell. It must
///     be pure in each other operation, because this code can call it more than one time for one
///     input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (cell: Cell<_>) = cell.MapImpl(Func<_, _> f)

/// <summary>
///     Applies a cell of functions to a cell of values.
/// </summary>
/// <param name="f">The cell holding the function to apply.</param>
/// <param name="cell">The cell holding the value to apply it to.</param>
/// <returns>
///     A cell whose value is the result of the current function in <paramref name="f" /> on the input
///     cell's current value.
/// </returns>
/// <remarks>
///     This is the primitive of all the <c>lift</c> functions. Use <c>lift2</c> and the other
///     <c>lift</c> functions first. Use this function only for the conditions that they do not cover.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let apply f (cell: Cell<_>) =
    cell.ApplyImpl(f |> map (fun f -> Func<_, _> f))

/// <summary>
///     Combines two cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the two
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 f (cell: Cell<_>, cell2) = cell.LiftImpl(cell2, Func<_, _, _> f)

/// <summary>
///     Combines three cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the three
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3 f (cell: Cell<_>, cell2, cell3) =
    cell.LiftImpl(cell2, cell3, Func<_, _, _, _> f)

/// <summary>
///     Combines four cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the four
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4 f (cell: Cell<_>, cell2, cell3, cell4) =
    cell.LiftImpl(cell2, cell3, cell4, Func<_, _, _, _, _> f)

/// <summary>
///     Combines five cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the five current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the five
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5 f (cell: Cell<_>, cell2, cell3, cell4, cell5) =
    cell.LiftImpl(cell2, cell3, cell4, cell5, Func<_, _, _, _, _, _> f)

/// <summary>
///     Combines six cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the six current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the six
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6 f (cell: Cell<_>, cell2, cell3, cell4, cell5, cell6) =
    cell.LiftImpl(cell2, cell3, cell4, cell5, cell6, Func<_, _, _, _, _, _, _> f)

/// <summary>
///     Combines seven cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the seven current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <param name="cell7">The seventh cell.</param>
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the seven
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
let lift7 f (cell, cell2, cell3, cell4, cell5, cell6, cell7) =
    ((cell, cell2, cell3, cell4, cell5, cell6) |> lift6 tuple6S, cell7)
    |> lift2 (fun struct (a, b, c, d, e, f') -> f a b c d e f')

/// <summary>
///     Combines eight cells into one whose value is a function of all of theirs.
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
/// <returns>
///     A cell whose value is the result of <paramref name="f" /> on the current values of the eight
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
let lift8 f (cell, cell2, cell3, cell4, cell5, cell6, cell7, cell8) =
    ((cell, cell2, cell3, cell4, cell5, cell6) |> lift6 tuple6S, cell7, cell8)
    |> lift3 (fun struct (a, b, c, d, e, f') -> f a b c d e f')

/// <summary>
///     Suppresses updates whose value the given function considers equal to the last one that got
///     through.
/// </summary>
/// <param name="compare">Gives true when this code must read two values as equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
///     A suppressed update is not a missing update. The cell takes the new value, and the next
///     comparer call reads the last value that the cell published.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithCompare compare (cell: Cell<_>) = cell.CalmImpl(Func<_, _, _> compare)

/// <summary>
///     Suppresses updates whose value the given comparer considers equal to the last one that got
///     through.
/// </summary>
/// <param name="equalityComparer">Gives true when two values are equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
///     A suppressed update is not a missing update. The cell takes the new value, and the next
///     comparer call reads the last value that the cell published.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithEqualityComparer (equalityComparer: IEqualityComparer<_>) (cell: Cell<_>) =
    cell.CalmImpl(Func<_, _, _>(fun x y -> equalityComparer.Equals(x, y)))

/// <summary>
///     Suppresses updates equal, by F#'s structural equality, to the last one that got through.
/// </summary>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
///     A suppressed update is not a missing update. The cell takes the new value, and the next
///     comparer call reads the last value that the cell published.
///
///     Uses <c>=</c>, so for a type without meaningful structural equality use
///     the alternative <c>calmWithCompare</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calm (cell: Cell<_>) = cell.CalmImpl(Func<_, _, _> (=))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllCollection f (cells: IReadOnlyCollection<'Cell>) =
    CellExtensionMethodsInternal.LiftCellsImpl(cells, (Func<_, _> f))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllSeq f (cells: seq<'Cell>) =
    CellExtensionMethodsInternal.LiftCellsImpl(cells, (Func<_, _> f))

/// <summary>
///     Combines any number of cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, in the sequence of the cells.</param>
/// <param name="cells">The cells to put together.</param>
/// <returns>A cell whose value is the result of <paramref name="f" /> on all the current values.</returns>
/// <remarks>
///     The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
///     in the same manner. At each count of the inputs that change in one transaction, the result
///     updates one time.
/// </remarks>
let liftAll f (cells: seq<'Cell>) =
    match cells with
    | :? IReadOnlyCollection<'Cell> as cells -> liftAllCollection f cells
    | cells -> liftAllSeq f cells

/// <summary>
///     Unwraps a cell of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="cell">The cell holding a behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchB cell =
    CellExtensionMethodsInternal.SwitchBImpl cell

/// <summary>
///     Unwraps a cell of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="cell">The cell that holds a second cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
/// <remarks>
///     This is how a graph changes its shape at run time. The external cell selects the internal
///     cell to follow.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchC cell =
    CellExtensionMethodsInternal.SwitchCImpl cell

/// <summary>
///     Unwraps a cell of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="cell">The cell holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
///     In the transaction where the cell changes, the result takes the firing from the stream at
///     the start of that transaction. It does not use the firing from the new stream.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchS cell =
    CellExtensionMethodsInternal.SwitchSImpl cell
