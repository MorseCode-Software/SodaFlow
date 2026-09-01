/// <summary>
///     Creating cells and combining them.
/// </summary>
/// <remarks>
///     A cell is a value that varies over time and also exposes the stream of its own changes.
///     Everything a behavior can do, a cell can do; a cell adds <c>updates</c> and <c>values</c>,
///     and the operations built on them. Where the changes are never needed, <c>Behavior</c> is
///     the cheaper choice.
///
///     Build the graph inside <c>Transaction.run</c> so that no first firing is missed. This
///     matters most with <c>values</c>, which always fires immediately: a listener attached in a
///     later transaction has already missed that firing.
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
///     For a constant that is expensive to produce, or that is not yet available when the graph is
///     being built - the value is forced only when the cell is first sampled.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let constantLazy value = CellInternal.ConstantLazyImpl value

/// <summary>
///     Builds a cell which refers to itself, closing the loop within one transaction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the cell it stands for and
///     anything else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the cell the forward reference was closed with, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     A cell defined in terms of itself needs a forward reference to exist before the value it
///     refers to does. Both the reference and its resolution must happen in a single transaction,
///     which this opens if none is running.
///
///     Use <c>loopWithNoCaptures</c> where nothing but the cell itself is needed.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let loop f =
    TransactionInternal.Apply(fun transaction _ ->
        let l = LoopedCell()
        let struct (s, r) = f l
        l.Loop(transaction, s)
        struct (s, r))

/// <summary>
///     Builds a self-referential cell where nothing but the cell itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the cell it stands for.</param>
/// <returns>The cell the forward reference was closed with.</returns>
/// <remarks>
///     <c>loop</c> where something more than the cell needs to escape the loop.
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
///     May be used inside the functions passed to the primitives that apply them to streams, where
///     it means the same as snapshotting. Outside a transaction it opens one of its own, so the
///     value read is never a half-updated one.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sample (cell: Cell<_>) = cell.SampleImpl()

/// <summary>
///     Gets a cell's value as of now, without forcing it yet.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>A lazy value which yields what the cell held at the moment of this call.</returns>
/// <remarks>
///     The value is pinned now and computed later, which is what the looping constructs need: at
///     the point a loop is being closed the value is not yet known, but which moment it is to be
///     taken from already is.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sampleLazy (cell: Cell<_>) = cell.SampleLazyImpl()

/// <summary>
///     Gets a stream firing the new value of a cell each time it changes.
/// </summary>
/// <param name="cell">The cell to observe.</param>
/// <returns>A stream firing the updated value, in the transaction the update happened in.</returns>
/// <remarks>
///     Does not fire for the value the cell starts with - only for changes. Use <c>values</c> to
///     get that initial value as a firing too.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let updates (cell: Cell<_>) = cell.UpdatesImpl

/// <summary>
///     Gets a stream firing the cell's current value at once, and its new value on every change.
/// </summary>
/// <param name="cell">The cell to observe.</param>
/// <returns>
///     A stream which fires the current value in the transaction this is called in, and then the
///     updated value on every change.
/// </returns>
/// <remarks>
///     The immediate firing happens in the transaction this is called in, so this must be called
///     inside <c>Transaction.run</c> if that firing is to be observed at all - a listener attached
///     afterward, in a later transaction, has already missed it. This is the single most common
///     reason to wrap graph construction in a transaction.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let values (cell: Cell<_>) = cell.ValuesImpl

/// <summary>
///     Views a cell as a behavior.
/// </summary>
/// <param name="cell">The cell to view.</param>
/// <returns>The same value, seen as a behavior, without the stream of its changes.</returns>
/// <remarks>
///     Nothing is created or converted; a cell already is a behavior with updates attached. This
///     is for passing one to something written against <c>Behavior</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let asBehavior (cell: Cell<_>) = cell.BehaviorImpl

/// <summary>
///     Listens for changes without keeping the cell alive.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>A weak listener, which may be stopped with <c>WeakListener.unlisten</c>.</returns>
/// <remarks>
///     The listener stops on its own once the cell is collected, which makes this the right choice
///     where there is no clean moment to stop listening: hold the returned handle as a field of the
///     object doing the listening, and the two go away together. Where the cell should be kept
///     alive for as long as something is listening, use <c>listenStrong</c>.
///
///     Fires the current value immediately, in the transaction this is called in.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listen handler (cell: Cell<_>) = cell.ListenImpl(Action<_> handler)

/// <summary>
///     Listens for changes, keeping the cell alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>
///     A strong listener, which may be stopped with <c>StrongListener.unlisten</c> or disposed.
/// </returns>
/// <remarks>
///     The listener roots the cell, so the graph behind it stays alive for as long as the returned
///     handle is reachable. Keep the handle and stop it when finished, or use <c>listen</c>
///     where there is no good moment to do that.
///
///     Fires the current value immediately, in the transaction this is called in. The handler runs
///     under the transaction lock, so it should return promptly.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenStrong handler (cell: Cell<_>) =
    cell.ListenStrongImpl(Action<_> handler)

/// <summary>
///     Transforms a cell with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="cell">The cell to transform.</param>
/// <returns>A cell whose value is <paramref name="f" /> applied to the input cell's current value.</returns>
/// <remarks>
///     <paramref name="f" /> may construct FRP logic or sample behaviors and cells; apart from
///     that it must be pure, since it may be called more than once for one input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (cell: Cell<_>) = cell.MapImpl(Func<_, _> f)

/// <summary>
///     Applies a cell of functions to a cell of values.
/// </summary>
/// <param name="f">The cell holding the function to apply.</param>
/// <param name="cell">The cell holding the value to apply it to.</param>
/// <returns>
///     A cell whose value is the current function in <paramref name="f" /> applied to the input
///     cell's current value.
/// </returns>
/// <remarks>
///     The primitive all of the <c>lift</c> functions are built from. Reach for <c>lift2</c> and
///     its siblings first; this is for the cases they do not cover.
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
///     A cell whose value is <paramref name="f" /> applied to the current values of the two
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 f ((cell: Cell<_>), cell2) = cell.LiftImpl(cell2, Func<_, _, _> f)

/// <summary>
///     Combines three cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <returns>
///     A cell whose value is <paramref name="f" /> applied to the current values of the three
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3 f ((cell: Cell<_>), cell2, cell3) =
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
///     A cell whose value is <paramref name="f" /> applied to the current values of the four
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4 f ((cell: Cell<_>), cell2, cell3, cell4) =
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
///     A cell whose value is <paramref name="f" /> applied to the current values of the five
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5 f ((cell: Cell<_>), cell2, cell3, cell4, cell5) =
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
///     A cell whose value is <paramref name="f" /> applied to the current values of the six
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6 f ((cell: Cell<_>), cell2, cell3, cell4, cell5, cell6) =
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
///     A cell whose value is <paramref name="f" /> applied to the current values of the seven
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
let lift7 f (cell, cell2, cell3, cell4, cell5, cell6, cell7) =
    ((cell, cell2, cell3, cell4, cell5, cell6) |> lift6 tuple6S, cell7)
    |> lift2 (fun struct (a, b, c, d, e, f') g -> f a b c d e f' g)

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
///     A cell whose value is <paramref name="f" /> applied to the current values of the eight
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
let lift8 f (cell, cell2, cell3, cell4, cell5, cell6, cell7, cell8) =
    ((cell, cell2, cell3, cell4, cell5, cell6) |> lift6 tuple6S, cell7, cell8)
    |> lift3 (fun struct (a, b, c, d, e, f') g h -> f a b c d e f' g h)

/// <summary>
///     Suppresses updates whose value the given comparison considers equal to the last one that got
///     through.
/// </summary>
/// <param name="compare">Returns whether two values are to be treated as equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
///     Suppressing an update is not the same as it not happening: the cell still takes the new value,
///     and the next comparison is made against the last value that got through.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithCompare compare (cell: Cell<_>) = cell.CalmImpl(Func<_, _, _> compare)

/// <summary>
///     Suppresses updates whose value the given comparer considers equal to the last one that got
///     through.
/// </summary>
/// <param name="equalityComparer">Decides whether two values are equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
///     Suppressing an update is not the same as it not happening: the cell still takes the new value,
///     and the next comparison is made against the last value that got through.
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
///     Suppressing an update is not the same as it not happening: the cell still takes the new value,
///     and the next comparison is made against the last value that got through.
///
///     Uses <c>=</c>, so for a type without meaningful structural equality use
///     <c>calmWithCompare</c> instead.
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
/// <param name="f">Combines the current values, given in the order the cells were supplied.</param>
/// <param name="cells">The cells to combine.</param>
/// <returns>A cell whose value is <paramref name="f" /> applied to all of the current values.</returns>
/// <remarks>
///     The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
///     in the same way: however many of the inputs change in one transaction, the result updates
///     once.
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
/// <param name="cell">The cell holding another cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
/// <remarks>
///     This is how a graph changes shape at run time: the outer cell chooses which inner one is
///     being followed.
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
///     On the transaction where the cell changes, the firing taken is the one from the stream held
///     at the start of that transaction, not the newly selected one.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchS cell =
    CellExtensionMethodsInternal.SwitchSImpl cell
