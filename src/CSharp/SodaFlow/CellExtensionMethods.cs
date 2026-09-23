using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="Cell{T}" />.
/// </summary>
/// <remarks>
///     A cell is a behavior that also gives the stream of its own changes. Thus, it has everything in
///     <see cref="BehaviorExtensionMethods" />, and the operations that use those updates. Build the graph in
///     a <see cref="Transaction.Run{T}(System.Func{T})" /> so that no first firing goes to the listener. That
///     is most important with <see cref="Values{T}" />, which always fires immediately.
/// </remarks>
[PublicAPI]
public static class CellExtensionMethods
{
    /// <summary>
    ///     Sample the current value of the cell.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <returns>The current value of the cell.</returns>
    /// <remarks>
    ///     <para>
    ///         The functions that the primitives give to a stream can call this method. There it is the same
    ///         as a snapshot of the cell. Those primitives are
    ///         <see cref="StreamExtensionMethods.Map{T, TResult}(Stream{T}, Func{T,TResult})" />,
    ///         <see cref=" StreamExtensionMethods.Snapshot{T, T2, TResult}(Stream{T}, Cell{T2}, Func{T, T2, TResult})" />,
    ///         <see cref="StreamExtensionMethods.Filter{T}(Stream{T}, Func{T, bool})" />, and
    ///         <see cref="StreamExtensionMethods.Merge{T}(Stream{T}, Stream{T}, Func{T, T, T})" />.
    ///     </para>
    ///     <para>
    ///         Usually, use <see cref="ListenStrong{T}(Cell{T}, Action{T})" /> as a replacement, thus the
    ///         code keeps each update. But this method is correct in many conditions.
    ///     </para>
    ///     <para>
    ///         It can be best to use this method in an explicit transaction (using
    ///         <see cref="Transaction.Run{T}(Func{T})" /> or <see cref="Transaction.RunVoid(Action)" />).
    ///         For example, a c.Sample() in an explicit transaction, with a c.Updates().ListenStrong(...),
    ///         captures the current value and each update. The code keeps each value between the two.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Sample<T>(this Cell<T> c) => c.SampleImpl();

    /// <summary>
    ///     Sample the current value of the cell lazily.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <returns>A lazy value that gives the current value of the cell.</returns>
    /// <remarks>
    ///     This is a variant of <see cref="Sample{T}" /> that works with the <see cref="CellLoop{T}" /> class
    ///     when no code closed the cell loop.  Use it in code that is general
    ///     sufficient for a <see cref="CellLoop{T}" /> argument.  See
    ///     <see cref="StreamExtensionMethods.HoldLazy{T}(Stream{T}, Lazy{T})" />.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Lazy<T> SampleLazy<T>(this Cell<T> c) => c.SampleLazyImpl();

    /// <summary>
    ///     Gets the stream of discrete updates to this cell.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <returns>
    ///     The stream of discrete updates to this cell.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Updates<T>(this Cell<T> c) => c.UpdatesImpl;

    /// <summary>
    ///     Gets the stream of values of this cell.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <returns>
    ///     The stream of values of this cell.
    /// </returns>
    /// <remarks>
    ///     This stream is the same as the stream from <see cref="Updates{T}(Cell{T})" />, but it also fires
    ///     during the transaction that gave it.
    ///     To see the first value, read this property and use it in the same explicit transaction.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Values<T>(this Cell<T> c) => c.ValuesImpl;

    /// <summary>
    ///     Return a reference to this <see cref="Cell{T}" /> as a <see cref="Behavior{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <returns>A reference to this <see cref="Cell{T}" /> as a <see cref="Behavior{T}" />.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> AsBehavior<T>(this Cell<T> c) => c.BehaviorImpl;

    /// <summary>
    ///     Listen for updates to the value of this cell, keeping the cell alive for as long as the returned
    ///     listener is reachable. A disposal of the <see cref="IStrongListener" /> from this call stops
    ///     the listener. This is an OPERATIONAL mechanism for the boundary between
    ///     the world of I/O and FRP.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <param name="handler">The handler to run with each value.</param>
    /// <returns>An <see cref="IStrongListener" />. A disposal of it stops the listener.</returns>
    /// <remarks>
    ///     <para>
    ///         Make no assumption about the thread that calls the handler, and the handler must not block.
    ///         The handler must not call <see cref="StreamSinkExtensionMethods.Send{T}" /> or
    ///         <see cref="CellSinkExtensionMethods.Send{T}" />.
    ///         They throw an exception, because this method is not for the definition of a new primitive.
    ///     </para>
    ///     <para>
    ///         With no disposal of the <see cref="IStrongListener" />, the listener continues until a
    ///         disposal of this cell, or until a GC collects it.
    ///     </para>
    ///     <para>
    ///         This roots the cell, thus a GC cannot collect the graph behind it while the listener from this call is
    ///         reachable.  Use <see cref="Listen{T}(Cell{T}, Action{T})" /> where that is not wanted.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IStrongListener ListenStrong<T>(this Cell<T> c, Action<T> handler) => c.ListenStrongImpl(handler);

    /// <summary>
    ///     Listen for updates to the value of this cell, without keeping the cell alive.  The returned
    ///     <see cref="IWeakListener" />. A call to <see cref="IListener.Unlisten" /> stops the listener,
    ///     and the listener also stops when a GC collects it.
    ///     This is an OPERATIONAL mechanism for interfacing between the world of I/O and FRP.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <param name="handler">The handler to run with each value.</param>
    /// <returns>
    ///     An <see cref="IWeakListener" />. A call to its <see cref="IListener.Unlisten" /> stops the
    ///     listener. Only <see cref="IStrongListener" /> is also an <see cref="IDisposable" />.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Make no assumption about the thread that calls the handler, and the handler must not block.
    ///         The handler must not call <see cref="StreamSinkExtensionMethods.Send{T}" /> or
    ///         <see cref="CellSinkExtensionMethods.Send{T}" />.
    ///         They throw an exception, because this method is not for the definition of a new primitive.
    ///     </para>
    ///     <para>
    ///         With no call to <see cref="IListener.Unlisten" />, the listener continues. It stops at a
    ///         disposal of this cell, when a GC collects the cell, or when a GC collects the listener.
    ///     </para>
    ///     <para>
    ///         This does not root the cell. Nothing here keeps the monitored graph in memory, thus the
    ///         listener stops when a GC collects the cell. Keep the listener from this call where that
    ///         matters, or use <see cref="ListenStrong{T}(Cell{T}, Action{T})" /> to keep the cell in memory.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IWeakListener Listen<T>(this Cell<T> c, Action<T> handler) => c.ListenImpl(handler);

    /// <summary>
    ///     Transforms the values of a cell with the given function. The cell from this call has the
    ///     value of that function on the value of the input cell.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <typeparam name="TResult">The type of values fired by the returned cell.</typeparam>
    /// <param name="c">The cell.</param>
    /// <param name="f">
    ///     Function to apply to change the values.  It must be a pure function.
    /// </param>
    /// <returns>A cell which fires values transformed by <paramref name="f" /> for each value fired by this
    /// cell.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Map<T, TResult>(this Cell<T> c, Func<T, TResult> f) => c.MapImpl(f);

    /// <summary>
    ///     Lift a binary function into cells, so the returned cell always reflects the specified function
    ///     applied to the input cells' values.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam> <typeparam name="T2">The type of second
    /// cell.</typeparam> <typeparam name="TResult">The type of the result.</typeparam> <param name="c">The
    /// cell.</param> <param name="c2">The second cell.</param> <param name="f">The binary function to lift
    /// into the cells.</param> <returns>A cell containing values resulting from the binary function applied
    /// to the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, T2, TResult>(this Cell<T> c, Cell<T2> c2, Func<T, T2, TResult> f) =>
        c.LiftImpl(b2: c2, f: f);

    /// <summary>
    ///     Lift a ternary function into cells, so the returned cell always reflects the specified function
    ///     applied to the input cells' values.
    /// </summary> <typeparam name="T">The type of the cell.</typeparam> <typeparam name="T2">The type of
    /// second cell.</typeparam> <typeparam name="T3">The type of third cell.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam> <param name="c">The cell.</param>
    /// <param name="c2">The second cell.</param> <param name="c3">The third cell.</param> <param name="f">The
    /// binary function to lift into the cells.</param> <returns>A cell containing values resulting from the
    /// ternary function applied to the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, T2, T3, TResult>(
        this Cell<T> c,
        Cell<T2> c2,
        Cell<T3> c3,
        Func<T, T2, T3, TResult> f) =>
        c.LiftImpl(b2: c2, b3: c3, f: f);

    /// <summary>
    ///     Lift a quaternary function into cells, so the returned cell always reflects the specified function
    ///     applied to the input cells' values.
    /// </summary> <typeparam name="T">The type of the cell.</typeparam> <typeparam name="T2">The type of
    /// second cell.</typeparam> <typeparam name="T3">The type of third cell.</typeparam>
    /// <typeparam name="T4">The type of fourth cell.</typeparam> <typeparam name="TResult">The type of the
    /// result.</typeparam> <param name="c">The cell.</param> <param name="c2">The second cell.</param>
    /// <param name="c3">The third cell.</param> <param name="c4">The fourth cell.</param>
    /// <param name="f">The binary function to lift into the cells.</param> <returns>A cell containing values
    /// resulting from the quaternary function applied to the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, T2, T3, T4, TResult>(
        this Cell<T> c,
        Cell<T2> c2,
        Cell<T3> c3,
        Cell<T4> c4,
        Func<T, T2, T3, T4, TResult> f) =>
        c.LiftImpl(b2: c2, b3: c3, b4: c4, f: f);

    /// <summary>
    ///     Lift a 5-argument function into cells, so the returned cell always reflects the specified function
    ///     applied to the input cells' values.
    /// </summary> <typeparam name="T">The type of the cell.</typeparam> <typeparam name="T2">The type of
    /// second cell.</typeparam> <typeparam name="T3">The type of third cell.</typeparam>
    /// <typeparam name="T4">The type of fourth cell.</typeparam> <typeparam name="T5">The type of fifth
    /// cell.</typeparam> <typeparam name="TResult">The type of the result.</typeparam> <param name="c">The
    /// cell.</param> <param name="c2">The second cell.</param> <param name="c3">The third cell.</param>
    /// <param name="c4">The fourth cell.</param> <param name="c5">The fifth cell.</param>
    /// <param name="f">The binary function to lift into the cells.</param> <returns>A cell containing values
    /// resulting from the 5-argument function applied to the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, T2, T3, T4, T5, TResult>(
        this Cell<T> c,
        Cell<T2> c2,
        Cell<T3> c3,
        Cell<T4> c4,
        Cell<T5> c5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        c.LiftImpl(b2: c2, b3: c3, b4: c4, b5: c5, f: f);

    /// <summary>
    ///     Lift a 6-argument function into cells, so the returned cell always reflects the specified function
    ///     applied to the input cells' values.
    /// </summary> <typeparam name="T">The type of the cell.</typeparam> <typeparam name="T2">The type of
    /// second cell.</typeparam> <typeparam name="T3">The type of third cell.</typeparam>
    /// <typeparam name="T4">The type of fourth cell.</typeparam> <typeparam name="T5">The type of fifth
    /// cell.</typeparam> <typeparam name="T6">The type of sixth cell.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam> <param name="c">The cell.</param>
    /// <param name="c2">The second cell.</param> <param name="c3">The third cell.</param>
    /// <param name="c4">The fourth cell.</param> <param name="c5">The fifth cell.</param>
    /// <param name="c6">The sixth cell.</param> <param name="f">The binary function to lift into the
    /// cells.</param> <returns>A cell containing values resulting from the 6-argument function applied to the
    /// input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, T2, T3, T4, T5, T6, TResult>(
        this Cell<T> c,
        Cell<T2> c2,
        Cell<T3> c3,
        Cell<T4> c4,
        Cell<T5> c5,
        Cell<T6> c6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        c.LiftImpl(b2: c2, b3: c3, b4: c4, b5: c5, b6: c6, f: f);

    /// <summary>
    ///     Apply a value in a cell to a function in a cell.  This is the primitive for all function lifting.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="c">The cell.</param>
    /// <param name="cf">The cell containing the function to apply the value to.</param>
    /// <returns>
    ///     A cell whose value is the result of applying the current function in cell <paramref name="cf" /> to this
    ///     cell's current value.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Apply<T, TResult>(this Cell<T> c, Cell<Func<T, TResult>> cf) => c.ApplyImpl(cf);

    /// <summary>
    ///     Return a cell whose stream only receives events which have a different value than the previous event.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam> <param name="c">The cell.</param> <returns>A
    /// cell whose stream only receives events which have a different value than the previous event.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> Calm<T>(this Cell<T> c) => c.CalmImpl(EqualityComparer<T>.Default.Equals);

    /// <summary>
    ///     Return a cell whose stream only receives events which have a different value than the previous event.
    /// </summary> <typeparam name="T">The type of the cell.</typeparam> <param name="c">The cell.</param>
    /// <param name="comparer">The equality comparer that gives true when two items are equal.</param>
    /// <returns>A cell whose stream only receives events which have a different value than the previous
    /// event.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> Calm<T>(this Cell<T> c, IEqualityComparer<T> comparer) => c.CalmImpl(comparer.Equals);

    /// <summary>
    ///     Return a cell whose stream only receives events which have a different value than the previous event.
    /// </summary> <typeparam name="T">The type of the cell.</typeparam> <param name="c">The cell.</param>
    /// <param name="areEqual">The function that gives true when two items are equal.</param> <returns>A
    /// cell whose stream only receives events which have a different value than the previous
    /// event.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> Calm<T>(this Cell<T> c, Func<T, T, bool> areEqual) => c.CalmImpl(areEqual);

    /// <summary>
    ///     Unwrap a behavior in a cell to give a time-varying behavior implementation.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <param name="cba">The cell containing a behavior.</param>
    /// <returns>The unwrapped behavior.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> SwitchB<T>(this Cell<Behavior<T>> cba) => cba.SwitchBImpl<T, Behavior<T>>();

    /// <summary>
    ///     Unwrap a cell in a second cell to give a time-varying cell implementation.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="cca">The cell that holds a second cell.</param>
    /// <returns>The unwrapped cell.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> SwitchC<T>(this Cell<Cell<T>> cca) => cca.SwitchCImpl<T, Cell<T>>();

    /// <summary>
    ///     Unwrap a stream in a cell to give a time-varying stream implementation. At a change to the
    ///     cell, the output stream fires the simultaneous firing, if there is one. That firing comes
    ///     from the stream that the cell held at the start of the transaction.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="csa">The cell containing the stream.</param>
    /// <returns>The unwrapped stream.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> SwitchS<T>(this Cell<Stream<T>> csa) => csa.SwitchSImpl<T, Stream<T>>();

    /// <summary>
    ///     Lift a function into a collection of cells, so the returned cell always reflects the specified
    ///     function applied to the input cells' values.
    /// </summary>
    /// <typeparam name="T">The type of the cells.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="c">The collection of cells.</param>
    /// <param name="f">The binary function to lift into the cells.</param>
    /// <returns>A cell containing values resulting from the function applied to the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, TResult>(
        this IEnumerable<Cell<T>> c,
        Func<IReadOnlyList<T>, TResult> f) =>
        c.LiftCellsImpl(f);

    /// <summary>
    ///     Lift a function into a collection of cells, so the returned cell always reflects the specified
    ///     function applied to the input cells' values.
    /// </summary>
    /// <typeparam name="T">The type of the cells.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="c">The collection of cells.</param>
    /// <param name="f">The binary function to lift into the cells.</param>
    /// <returns>A cell containing values resulting from the function applied to the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TResult> Lift<T, TResult>(
        this IReadOnlyCollection<Cell<T>> c,
        Func<IReadOnlyList<T>, TResult> f) =>
        c.LiftCellsImpl(f);

    /// <summary>
    ///     Lift into a collection of cells, so the returned cell always reflects a list of the input cells' values.
    /// </summary>
    /// <typeparam name="T">The type of the cells.</typeparam>
    /// <param name="c">The collection of cells.</param>
    /// <returns>A cell containing a list of the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<IReadOnlyList<T>> Lift<T>(this IEnumerable<Cell<T>> c) =>
        c.LiftCellsImpl<T, Cell<T>, IReadOnlyList<T>>(static v => v);

    /// <summary>
    ///     Lift into a collection of cells, so the returned cell always reflects a list of the input cells' values.
    /// </summary>
    /// <typeparam name="T">The type of the cells.</typeparam>
    /// <param name="c">The collection of cells.</param>
    /// <returns>A cell containing a list of the input cells' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<IReadOnlyList<T>> Lift<T>(this IReadOnlyCollection<Cell<T>> c) =>
        c.LiftCellsImpl<T, Cell<T>, IReadOnlyList<T>>(static v => v);
}
