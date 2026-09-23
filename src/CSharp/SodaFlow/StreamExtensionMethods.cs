using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="Stream{T}" />.
/// </summary>
/// <remarks>
///     A stream is a sequence of discrete firings. These are extension methods rather than instance
///     members, thus the combinators live out of the small assembly that holds the FRP engine. The
///     effect at the call site is the same.
///     Build the graph in a <see cref="Transaction.Run{T}(System.Func{T})" />, thus the listener gets
///     the first firing.
/// </remarks>
[PublicAPI]
public static class StreamExtensionMethods
{
    /// <summary>
    ///     Listen for events/firings on this stream, keeping the stream alive for as long as the returned
    ///     listener is reachable. A disposal of the <see cref="IStrongListener" /> from this call stops the
    ///     listener. This is an OPERATIONAL mechanism for the boundary between the world of I/O and FRP.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="handler">The handler to run with each value the stream fires.</param>
    /// <returns>An <see cref="IStrongListener" />. A disposal of it stops the listener.</returns>
    /// <remarks>
    ///     <para>
    ///         Make no assumption about the thread that calls the handler, and the handler must not block.
    ///         The handler must not call <see cref="StreamSinkExtensionMethods.Send{T}" /> or
    ///         <see cref="CellSinkExtensionMethods.Send{T}" />. They throw an exception, because this method
    ///         is not for the definition of a new primitive.
    ///     </para>
    ///     <para>
    ///         With no disposal of the <see cref="IStrongListener" />, the listener continues until a
    ///         disposal of this stream, or until a GC collects it.
    ///     </para>
    ///     <para>
    ///         Give the listener from this call to <see cref="AttachListener{T}" /> on this stream. A
    ///         disposal of this <see cref="IStrongListener" /> then occurs at the disposal of that stream,
    ///         and at the moment a GC collects it.
    ///     </para>
    ///     <para>
    ///         This roots the stream, thus a GC cannot collect the graph behind it while the listener from
    ///         this call is reachable. Use <see cref="Listen{T}(Stream{T}, Action{T})" /> where that is not
    ///         wanted.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IStrongListener ListenStrong<T>(this Stream<T> s, Action<T> handler) => s.ListenStrongImpl(handler);

    /// <summary>
    ///     Listen for events/firings on this stream, without keeping the stream alive. The returned
    ///     <see cref="IWeakListener" />. A call to <see cref="IListener.Unlisten" /> stops the listener, and
    ///     the listener also stops when a GC collects it. This is an OPERATIONAL mechanism for interfacing
    ///     between the world of I/O and FRP.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="handler">The handler to run with each value the stream fires.</param>
    /// <returns>
    ///     An <see cref="IWeakListener" />. A call to its <see cref="IListener.Unlisten" /> stops the
    ///     listener. Only <see cref="IStrongListener" /> is also an <see cref="IDisposable" />.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Make no assumption about the thread that calls the handler, and the handler must not block.
    ///         The handler must not call <see cref="StreamSinkExtensionMethods.Send{T}" /> or
    ///         <see cref="CellSinkExtensionMethods.Send{T}" />. They throw an exception, because this method
    ///         is not for the definition of a new primitive.
    ///     </para>
    ///     <para>
    ///         With no call to <see cref="IListener.Unlisten" />, the listener continues. It stops at a
    ///         disposal of this stream, when a GC collects the stream, or when a GC collects the listener.
    ///     </para>
    ///     <para>
    ///         Give the listener from this call to <see cref="AttachListener{T}" /> on this stream. This
    ///         <see cref="IWeakListener" /> then stops at the disposal of that stream, and at the moment a GC
    ///         collects it.
    ///     </para>
    ///     <para>
    ///         This does not root the stream. Nothing here keeps the monitored graph in memory, thus the
    ///         listener stops when a GC collects the stream. Keep the listener from this call where that
    ///         matters, or use <see cref="ListenStrong{T}(Stream{T}, Action{T})" /> to keep the stream in
    ///         memory.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IWeakListener Listen<T>(this Stream<T> s, Action<T> handler) => s.ListenImpl(handler);

    /// <summary>
    ///     Attaches a listener to this stream, thus a GC does not collect the listener before it collects this stream.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="listener">The listener to garbage collect along with this stream.</param>
    /// <returns>
    ///     A new stream equivalent to this stream which will garbage collect <paramref name="listener" /> when it is
    ///     garbage collected.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> AttachListener<T>(this Stream<T> s, IListener listener) => s.AttachListenerImpl(listener);

    /// <summary>
    ///     Handle the first event on this stream and then automatically unregister, without keeping the
    ///     stream alive.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="handler">The handler to run with the first value this stream fires.</param>
    /// <returns>
    ///     An <see cref="IWeakListener" />. A call to its <see cref="IListener.Unlisten" /> stops the
    ///     listener before that first event arrives.
    /// </returns>
    /// <remarks>
    ///     This does not root the stream, and the listener from this call is the only thing that keeps
    ///     the handler in memory. Keep it until that first event arrives. Code that discards it compiles,
    ///     and the handler then runs or does not run, which is dependent on the moment the GC collects.
    ///     Use <see cref="ListenOnceStrong{T}(Stream{T}, Action{T})" /> where the caller does not keep
    ///     the listener.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IWeakListener ListenOnce<T>(this Stream<T> s, Action<T> handler) => s.ListenOnceImpl(handler);

    /// <summary>
    ///     Handle the first event on this stream and then automatically unregister, keeping the stream
    ///     alive until that first event arrives.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="handler">The handler to run with the first value this stream fires.</param>
    /// <returns>
    ///     An <see cref="IStrongListener" />. A disposal of it stops the listener before that first event
    ///     arrives.
    /// </returns>
    /// <remarks>
    ///     This roots the stream until that first event arrives. Thus, the handler runs when the caller
    ///     discards the listener from this call. The root ends with that first event, or with an earlier
    ///     <see cref="IListener.Unlisten" />. Use <see cref="ListenOnce{T}(Stream{T}, Action{T})" /> where
    ///     the listener must not extend the lifetime of what it monitors.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IStrongListener ListenOnceStrong<T>(this Stream<T> s, Action<T> handler) =>
        s.ListenOnceStrongImpl(handler);

    /// <summary>
    ///     Handle the first event on this stream and then automatically unregister.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <returns>A task that completes at the next firing of this stream.</returns>
    public static Task<T> ListenOnceAsync<T>(this Stream<T> s) => s.ListenOnceAsync(CancellationToken.None);

    /// <summary>
    ///     Handle the first event on this stream and then automatically unregister.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>A task that completes at the next firing of this stream.</returns>
    public static Task<T> ListenOnceAsync<T>(this Stream<T> s, CancellationToken token)
    {
#if NETSTANDARD2_0_OR_GREATER || NET461_OR_GREATER || NET
        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
#else
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
#endif

        IStrongListener? listener = null;
        bool unlistenEarly = false;

        IStrongListener listenerToReturn =
            s.ListenStrong(a =>
            {
                // ReSharper disable once AccessToModifiedClosure
                IStrongListener? listenerLocal = listener;

                if (listenerLocal == null)
                {
                    unlistenEarly = true;
                }
                else
                {
                    listenerLocal.Unlisten();
                    listener = null;
                }

                tcs.TrySetResult(a);
            });

        listener = listenerToReturn;

        if (unlistenEarly)
        {
            listenerToReturn.Unlisten();
            listener = null;
        }

        token.Register(() =>
        {
            IStrongListener? listenerLocal = listener;
            listenerLocal?.Unlisten();
            listener = null;

            tcs.TrySetCanceled();
        });

#if NETSTANDARD2_0_OR_GREATER || NET461_OR_GREATER || NET
        return tcs.Task;
#else
            async Task<T> Execute(TaskCompletionSource<T> tcs2)
            {
                T result = await tcs2.Task;
                await Task.Yield();
                return result;
            }

            return Execute(tcs);
#endif
    }

    /// <summary>
    ///     Transforms the values of a stream with the given function. The stream from this call has
    ///     the value of that function on the value of the input stream.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TResult">The type of values fired by the returned stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="f">
    ///     Function to apply to change the values. It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}(Cell{T})" />, in which case it is equivalent to calling
    ///     <see cref="Snapshot{T, TResult}(Stream{T}, Cell{TResult})" /> on the cell. Other than this, the
    ///     function must be a pure function.
    /// </param>
    /// <returns>A stream which fires values transformed by <paramref name="f" /> for each value fired by this
    /// stream.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Map<T, TResult>(this Stream<T> s, Func<T, TResult> f) => s.MapImpl(f);

    /// <summary>
    ///     Transform the stream values to the specified constant value.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TResult">The type of the constant value fired by the returned stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="value">
    ///     The constant value to return from this mapping.
    /// </param>
    /// <returns>A stream which fires the constant value for each value fired by this stream.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> MapTo<T, TResult>(this Stream<T> s, TResult value) => s.MapToImpl(value);

    /// <summary>
    ///     Makes a cell with the given initial value, and the values of this stream update it.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="initialValue">The initial value of the cell.</param>
    /// <returns>A cell with the given initial value, which the values of this stream update.</returns>
    /// <remarks>
    ///     There is an implicit interval. State updates from stream event firings do not become
    ///     available as the current value of the cell to
    ///     <see cref="StreamExtensionMethods.Snapshot{T, T2, TResult}(Stream{T}, Cell{T2}, Func{T, T2, TResult})" />
    ///     until the next transaction. In different words,
    ///     <see cref="StreamExtensionMethods.Snapshot{T, T2, TResult}(Stream{T}, Cell{T2}, Func{T, T2, TResult})" />
    ///     always sees the value of a cell as it was before any state changes from the current transaction.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> Hold<T>(this Stream<T> s, T initialValue) => s.HoldImpl(initialValue);

    /// <summary>
    ///     Makes a cell with the given lazy initial value, and the values of this stream update it.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="initialValue">The lazily initialized initial value of the cell.</param>
    /// <returns>A cell with the given lazy initial value, which the values of this stream update.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> HoldLazy<T>(this Stream<T> s, Lazy<T> initialValue) => s.HoldLazyImpl(initialValue);

    /// <summary>
    ///     Gives a stream that fires the value of the cell at the moment of each firing.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="c">The cell to put together with this one.</param>
    /// <returns>A stream that fires the value of the cell at the moment of each firing.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, TResult>(this Stream<T> s, Cell<TResult> c) => s.SnapshotImpl(c);

    /// <summary>
    ///     Gives a stream that fires the value of the behavior at the moment of each firing.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="b">The behavior to put together with this one.</param>
    /// <returns>A stream that fires the value of the behavior at the moment of each firing.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, TResult>(this Stream<T> s, Behavior<TResult> b) => s.SnapshotImpl(b);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the value
    ///     of the cell.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the cell.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="c">The cell to put together with this one.</param>
    /// <param name="f">A function to change the stream value and cell value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the value of the
    ///     cell.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, TResult>(this Stream<T> s, Cell<T1> c, Func<T, T1, TResult> f) =>
        s.SnapshotImpl(c: c, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the value
    ///     of the behavior.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the behavior.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="b">The behavior to put together with this one.</param>
    /// <param name="f">A function to change the stream value and behavior value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the value of the
    ///     behavior.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, TResult>(
        this Stream<T> s,
        Behavior<T1> b,
        Func<T, T1, TResult> f) =>
        s.SnapshotImpl(b: b, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the values
    ///     of the cells.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the first cell.</typeparam>
    /// <typeparam name="T2">The type of the second cell.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="c1">The first cell to put together with this one.</param>
    /// <param name="c2">The second cell to put together with this one.</param>
    /// <param name="f">A function to change the stream value and cell value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the values of
    ///     the cells.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, T2, TResult>(
        this Stream<T> s,
        Cell<T1> c1,
        Cell<T2> c2,
        Func<T, T1, T2, TResult> f) =>
        s.SnapshotImpl(c1: c1, c2: c2, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the values
    ///     of the behaviors.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the first behavior.</typeparam>
    /// <typeparam name="T2">The type of the second behavior.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="b1">The first behavior to put together with this one.</param>
    /// <param name="b2">The second behavior to put together with this one.</param>
    /// <param name="f">A function to change the stream value and behavior value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the values of
    ///     the behaviors.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, T2, TResult>(
        this Stream<T> s,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T, T1, T2, TResult> f) =>
        s.SnapshotImpl(b1: b1, b2: b2, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the values
    ///     of the cells.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the first cell.</typeparam>
    /// <typeparam name="T2">The type of the second cell.</typeparam>
    /// <typeparam name="T3">The type of the third cell.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="c1">The first cell to put together with this one.</param>
    /// <param name="c2">The second cell to put together with this one.</param>
    /// <param name="c3">The third cell to put together with this one.</param>
    /// <param name="f">A function to change the stream value and cell value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the values of
    ///     the cells.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, T2, T3, TResult>(
        this Stream<T> s,
        Cell<T1> c1,
        Cell<T2> c2,
        Cell<T3> c3,
        Func<T, T1, T2, T3, TResult> f) =>
        s.SnapshotImpl(c1: c1, c2: c2, c3: c3, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the values
    ///     of the behaviors.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the first behavior.</typeparam>
    /// <typeparam name="T2">The type of the second behavior.</typeparam>
    /// <typeparam name="T3">The type of the third behavior.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="b1">The first behavior to put together with this one.</param>
    /// <param name="b2">The second behavior to put together with this one.</param>
    /// <param name="b3">The third behavior to put together with this one.</param>
    /// <param name="f">A function to change the stream value and behavior value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the values of
    ///     the behaviors.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, T2, T3, TResult>(
        this Stream<T> s,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T1, T2, T3, TResult> f) =>
        s.SnapshotImpl(b1: b1, b2: b2, b3: b3, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the values
    ///     of the cells.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the first cell.</typeparam>
    /// <typeparam name="T2">The type of the second cell.</typeparam>
    /// <typeparam name="T3">The type of the third cell.</typeparam>
    /// <typeparam name="T4">The type of the fourth cell.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="c1">The first cell to put together with this one.</param>
    /// <param name="c2">The second cell to put together with this one.</param>
    /// <param name="c3">The third cell to put together with this one.</param>
    /// <param name="c4">The fourth cell to put together with this one.</param>
    /// <param name="f">A function to change the stream value and cell value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the values of
    ///     the cells.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, T2, T3, T4, TResult>(
        this Stream<T> s,
        Cell<T1> c1,
        Cell<T2> c2,
        Cell<T3> c3,
        Cell<T4> c4,
        Func<T, T1, T2, T3, T4, TResult> f) =>
        s.SnapshotImpl(c1: c1, c2: c2, c3: c3, c4: c4, f: f);

    /// <summary>
    ///     Gives a stream that fires the result of the given function on the fired value and the values
    ///     of the behaviors.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="T1">The type of the first behavior.</typeparam>
    /// <typeparam name="T2">The type of the second behavior.</typeparam>
    /// <typeparam name="T3">The type of the third behavior.</typeparam>
    /// <typeparam name="T4">The type of the fourth behavior.</typeparam>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="b1">The first behavior to put together with this one.</param>
    /// <param name="b2">The second behavior to put together with this one.</param>
    /// <param name="b3">The third behavior to put together with this one.</param>
    /// <param name="b4">The fourth behavior to put together with this one.</param>
    /// <param name="f">A function to change the stream value and behavior value into a return value.</param>
    /// <returns>
    ///     A stream that fires the result of the given function on the fired value and the values of
    ///     the behaviors.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Snapshot<T, T1, T2, T3, T4, TResult>(
        this Stream<T> s,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T1, T2, T3, T4, TResult> f) =>
        s.SnapshotImpl(b1: b1, b2: b2, b3: b3, b4: b4, f: f);

    /// <summary>
    ///     Merges this stream with a second stream and drops the value of the second stream when they are
    ///     simultaneous.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="s2">The stream to merge with.</param>
    /// <returns>
    ///     A stream from a merge of this stream with a second stream. It drops the value of the second stream
    ///     when the two are simultaneous.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Where two stream events are simultaneous, which means that the two are in the same
    ///         transaction, the event value from this stream has precedence. The result drops the event value
    ///         from <paramref name="s2" />. To specify a custom combining function, use
    ///         <see cref="StreamExtensionMethods.Merge{T}(Stream{T}, Stream{T}, Func{T, T, T})" />.
    ///         s1.OrElse(s2) is equivalent to s1.Merge(s2, (l, r) =&gt; l).
    ///     </para>
    ///     <para>
    ///         This has the name OrElse and not Merge, to make it clear that a precaution is necessary,
    ///         because the result can drop a stream event.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> OrElse<T>(this Stream<T> s, Stream<T> s2) => s.OrElseImpl(s2);

    /// <summary>
    ///     Merges two streams of the same type into one. An event value on one input goes to the
    ///     stream from this call.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="s2">The stream to merge this stream with.</param>
    /// <param name="f">
    ///     Function to put the values together. It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}" />. Apart from this the function must be
    ///     pure.
    /// </param>
    /// <returns>
    ///     A stream which is the combination of event values from this stream and stream <paramref name="s2" />.
    /// </returns>
    /// <remarks>
    ///     The events are simultaneous when one event from this stream and one event from
    ///     <paramref name="s2" /> occur in the same transaction. The given function then puts the two
    ///     together into one. Thus, the stream from this call always has one event or none in each
    ///     transaction. The event from this stream goes to the left input of that function. The event
    ///     from <paramref name="s2" /> goes to the right.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Merge<T>(this Stream<T> s, Stream<T> s2, Func<T, T, T> f) => s.MergeImpl(s: s2, f: f);

    /// <summary>
    ///     Return a stream that only outputs events for which the predicate returns <code>true</code>.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="predicate">The predicate used to filter the stream.</param>
    /// <returns>A stream that only outputs events for which the predicate returns <code>true</code>.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Filter<T>(this Stream<T> s, Func<T, bool> predicate) => s.FilterImpl(predicate);

    /// <summary>
    ///     Return a stream that only outputs events from the input stream when the specified cell's value is
    ///     <code>true</code>.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="c">The cell that acts as a gate.</param>
    /// <returns>
    ///     A stream that only outputs events from the input stream when the specified cell's value is
    ///     <code>true</code>.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Gate<T>(this Stream<T> s, Cell<bool> c) => s.GateImpl(c);

    /// <summary>
    ///     Return a stream that only outputs events from the input stream when the specified behavior's value is
    ///     <code>true</code>
    ///     .
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="b">The behavior that acts as a gate.</param>
    /// <returns>
    ///     A stream that only outputs events from the input stream when the specified behavior's value is
    ///     <code>true</code>.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Gate<T>(this Stream<T> s, Behavior<bool> b) => s.GateImpl(b);

    /// <summary>
    ///     Return a stream that only outputs events which have a different value than the previous event.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <returns>A stream that only outputs events which have a different value than the previous event.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Calm<T>(this Stream<T> s) => s.CalmImpl(EqualityComparer<T>.Default.Equals);

    /// <summary>
    ///     Return a stream that only outputs events which have a different value than the previous event.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="comparer">The equality comparer that gives true when two items are equal.</param>
    /// <returns>A stream that only outputs events which have a different value than the previous event.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Calm<T>(this Stream<T> s, IEqualityComparer<T> comparer) => s.CalmImpl(comparer.Equals);

    /// <summary>
    ///     Return a stream that only outputs events which have a different value than the previous event.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="areEqual">The function that gives true when two items are equal.</param>
    /// <returns>A stream that only outputs events which have a different value than the previous event.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Calm<T>(this Stream<T> s, Func<T, T, bool> areEqual) => s.CalmImpl(areEqual);

    /// <summary>
    ///     Transform a stream with a generalized state loop (a Mealy machine). This calls the function with
    ///     the input and the previous state, and the function returns the new state and the output value.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TState">The type of the state of the Mealy machine.</typeparam>
    /// <typeparam name="TReturn">The type of the return value.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="initialState">The initial state of the Mealy machine.</param>
    /// <param name="f">
    ///     Function to apply to update the state.  It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}" />, and this is then equivalent to snapshotting the cell with
    ///     <see cref="Snapshot{T, TReturn}(Stream{T}, Cell{TReturn})" />.  Apart from this, the function must be pure.
    /// </param>
    /// <returns>A stream resulting from the transformation of this stream by the Mealy machine.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TReturn> Collect<T, TState, TReturn>(
        this Stream<T> s,
        TState initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        s.CollectImpl(initialState: initialState, f: f);

    /// <summary>
    ///     Transform a stream with a generalized state loop (a Mealy machine) using a lazily evaluated
    ///     initial state. This calls the function with the input and the previous state, and the function
    ///     returns the new state and the output value.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TState">The type of the state of the Mealy machine.</typeparam>
    /// <typeparam name="TReturn">The type of the return value.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="initialState">The lazily evaluated initial state of the Mealy machine.</param>
    /// <param name="f">
    ///     Function to apply to update the state.  It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}" />, and this is then equivalent to snapshotting the cell with
    ///     <see cref="Snapshot{T, TReturn}(Stream{T}, Cell{TReturn})" />.  Apart from this, the function must be pure.
    /// </param>
    /// <returns>A stream resulting from the transformation of this stream by the Mealy machine.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TReturn> CollectLazy<T, TState, TReturn>(
        this Stream<T> s,
        Lazy<TState> initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        s.CollectLazyImpl(initialState: initialState, f: f);

    /// <summary>
    ///     Accumulate on this stream, outputting the new state each time an event fires.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TReturn">The type of the accumulated state.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="initialState">The initial state.</param>
    /// <param name="f">
    ///     Function to apply to update the state.  It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}" />, and this is then equivalent to snapshotting the cell with
    ///     <see cref="Snapshot{T, TReturn}(Stream{T}, Cell{TReturn})" />.  Apart from this, the function must be pure.
    /// </param>
    /// <returns>A cell holding the accumulated state of this stream.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TReturn> Accum<T, TReturn>(
        this Stream<T> s,
        TReturn initialState,
        Func<T, TReturn, TReturn> f) =>
        s.AccumImpl(initialState: initialState, f: f);

    /// <summary>
    ///     Accumulate on this stream, outputting the new state each time an event fires using a lazily
    ///     evaluated initial state.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TReturn">The type of the accumulated state.</typeparam>
    /// <param name="s">The stream.</param>
    /// <param name="initialState">The lazily evaluated initial state.</param>
    /// <param name="f">
    ///     Function to apply to update the state.  It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}" />, and this is then equivalent to snapshotting the cell with
    ///     <see cref="Snapshot{T, TReturn}(Stream{T}, Cell{TReturn})" />.  Apart from this, the function must be pure.
    /// </param>
    /// <returns>A cell holding the accumulated state of this stream.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TReturn> AccumLazy<T, TReturn>(
        this Stream<T> s,
        Lazy<TReturn> initialState,
        Func<T, TReturn, TReturn> f) =>
        s.AccumLazyImpl(initialState: initialState, f: f);

    /// <summary>
    ///     Return a stream that outputs only one value: the next event of the input stream starting from the
    ///     transaction of this call.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="s">The stream.</param>
    /// <returns>
    ///     A stream that outputs only one value: the next event of the input stream starting from the
    ///     transaction of this call.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Once<T>(this Stream<T> s) => s.OnceImpl();

    /// <summary>
    ///     Merges a collection of streams and drops the value of the stream earlier in the collection
    ///     when they are simultaneous.
    /// </summary>
    /// <param name="s">The collection of streams to merge.</param>
    /// <returns>
    ///     A stream from a merge of the collection of streams. It drops the value of the stream
    ///     earlier in the collection when the two are simultaneous.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> OrElse<T>(this IEnumerable<Stream<T>> s) => s.OrElseImpl<T, Stream<T>>();

    /// <summary>
    ///     Merges a collection of streams of the same type into one. An event on each input goes to
    ///     the stream from this call.
    /// </summary>
    /// <param name="s">The collection of streams to merge.</param>
    /// <param name="f">
    ///     Function to put the values together. It can make FRP logic or use
    ///     <see cref="CellExtensionMethods.Sample{T}" />. Apart from this the function must be
    ///     pure.
    /// </param>
    /// <returns>
    ///     A stream which is the combination of event values from the collection of streams <paramref name="s" />.
    /// </returns>
    /// <remarks>
    ///     The events are simultaneous when more than one stream has an event in the same
    ///     transaction. The given function then puts them together into one. Thus, the stream from
    ///     this call always has one event or none in each transaction. The event from the stream
    ///     earlier in the collection goes to the left input of that function. The event from the
    ///     stream after it goes to the right.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> Merge<T>(this IEnumerable<Stream<T>> s, Func<T, T, T> f) => s.MergeImpl(f);

    /// <summary>
    ///     Return a stream that only outputs events that have values, removing the <see cref="Maybe{T}" /> wrapper, and
    ///     discarding <see cref="Maybe.None" /> values.
    /// </summary>
    /// <param name="s">The stream of <see cref="Maybe{T}" /> values to filter.</param>
    /// <returns>
    ///     A stream that only outputs events that have values, removing the <see cref="Maybe{T}" /> wrapper, and
    ///     discarding <see cref="Maybe.None" /> values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> FilterSome<T>(this Stream<Maybe<T>> s) =>
        s.FilterSomeImpl<T, Maybe<T>>(static (m, a) => m.MatchSome(a));

    /// <summary>
    ///     Transform the stream values with a function which can give no value, and fire only the values it gives.
    /// </summary>
    /// <param name="s">The stream to transform.</param>
    /// <param name="f">
    ///     Function to apply to each value. It can make FRP logic or use <see cref="CellExtensionMethods.Sample{T}" />.
    ///     Apart
    ///     from this the function must be pure.
    /// </param>
    /// <typeparam name="T">The type of the values fired by the stream to transform.</typeparam>
    /// <typeparam name="TResult">The type of the values fired by the returned stream.</typeparam>
    /// <returns>
    ///     A stream that fires the value from <paramref name="f" />, for each firing of
    ///     <paramref name="s" /> that gives one. It does not fire for the other firings.
    /// </returns>
    /// <remarks>
    ///     Maps and filters in one step. Use it for the usual condition where a test of an event is
    ///     the same work as the value that goes through. Examples are a parse, a lookup, and a
    ///     narrower type. This is <c>s.Map(f).FilterSome()</c>, with the same transaction semantics.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<TResult> Choose<T, TResult>(this Stream<T> s, Func<T, Maybe<TResult>> f) =>
        s.MapImpl(f).FilterSomeImpl<TResult, Maybe<TResult>>(static (m, a) => m.MatchSome(a));
}
