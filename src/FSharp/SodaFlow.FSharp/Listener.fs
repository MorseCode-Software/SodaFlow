namespace SodaFlow

/// <summary>
///     Stopping listeners, and combining some of them into one.
/// </summary>
/// <remarks>
///     A listener is the handle returned by <c>Stream.listen</c>, <c>Stream.listenStrong</c> and
///     their cell equivalents. This module operates on the general <c>IListener</c>. See
///     <c>WeakListener</c> and <c>StrongListener</c> for the versions that give
///     <c>IWeakListener</c> and <c>IStrongListener</c>, and <c>MutableListener</c> for a handle
///     that can point to a different listener.
/// </remarks>
module Listener =
    open System.Runtime.CompilerServices

    /// <summary>
    ///     Stops listening, through a handle which does not itself keep the observed stream alive.
    /// </summary>
    /// <param name="listener">The listener to stop.</param>
    /// <remarks>
    ///     A second call is safe. Each call after the first does nothing.
    /// </remarks>
    let unlistenWeak (listener: IListenerWithWeakReference) = listener.Unlisten()
    /// <summary>
    ///     Stops listening.
    /// </summary>
    /// <param name="listener">The listener to stop.</param>
    /// <remarks>
    ///     A second call is safe. Each call after the first does nothing.
    /// </remarks>
    let unlisten (listener: IListener) = listener.Unlisten()
    /// <summary>
    ///     Gets a view of a listener which does not keep the stream it observes alive.
    /// </summary>
    /// <param name="listener">The listener to give a weak reference to.</param>
    /// <returns>
    ///     A handle for <c>unlistenWeak</c>. It does not by itself keep the monitored stream in memory.
    /// </returns>
    /// <remarks>
    ///     The listener from <c>Stream.listenStrong</c> keeps the stream that it monitors in memory for
    ///     as long as code can get to the listener. Thus, a part of the graph stays rooted while a
    ///     listener monitors it. This gives a handle that does not root the graph. Use it where the
    ///     graph must not stay in memory.
    /// </remarks>
    let getListenerWithWeakReference (listener: IListener) = listener.GetListenerWithWeakReference()

    /// <summary>
    ///     A listener which is not listening to anything, and whose <c>unlisten</c> does nothing.
    /// </summary>
    /// <remarks>
    ///     The identity for <c>append</c>, and what to return from a branch which has nothing to
    ///     unsubscribe.
    /// </remarks>
    let empty = ListenerInternal.EmptyImpl

    /// <summary>
    ///     Combines a list of listeners into one which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>A listener whose <c>unlisten</c> stops each listener in the list.</returns>
    /// <remarks>
    ///     This code reads the list at this moment, and does not monitor it for a change.
    /// </remarks>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let fromList listeners =
        ListenerInternal.CreateCompositeImpl listeners

    /// <summary>
    ///     Combines a list of weak listeners into one weak listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>An <c>IWeakListener</c> whose <c>unlisten</c> stops each listener in the list.</returns>
    /// <remarks>
    ///     Like the listeners it combines, the result does not keep the observed streams alive.
    /// </remarks>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let fromWeakList listeners =
        ListenerInternal.CreateWeakCompositeImpl listeners

    /// <summary>
    ///     Combines a list of strong listeners into one strong listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>
    ///     An <c>IStrongListener</c> whose <c>unlisten</c> stops each listener in the list, and
    ///     and a disposal does the same.
    /// </returns>
    /// <remarks>
    ///     As the listeners in it do, the result keeps the monitored streams in memory until a call
    ///     stops it, or a disposal stops it.
    /// </remarks>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let fromStrongList listeners =
        ListenerInternal.CreateStrongCompositeImpl listeners

    /// <summary>
    ///     Combines a sequence of listeners into one which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>A listener whose <c>unlisten</c> stops each listener in the sequence.</returns>
    /// <remarks>
    ///     This code enumerates the sequence one time, immediately. Thus, a lazy sequence is correct,
    ///     and a sequence that reads one time is also correct.
    /// </remarks>
    let fromSeq listeners = List.ofSeq listeners |> fromList
    /// <summary>
    ///     Combines a sequence of weak listeners into one weak listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>An <c>IWeakListener</c> whose <c>unlisten</c> stops each listener in the sequence.</returns>
    /// <remarks>
    ///     This code enumerates the sequence one time, immediately.
    /// </remarks>
    let fromWeakSeq listeners = List.ofSeq listeners |> fromWeakList
    /// <summary>
    ///     Combines a sequence of strong listeners into one strong listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>An <c>IStrongListener</c> whose <c>unlisten</c> stops each listener in the sequence.</returns>
    /// <remarks>
    ///     This code enumerates the sequence one time, immediately.
    /// </remarks>
    let fromStrongSeq listeners = List.ofSeq listeners |> fromStrongList

    /// <summary>
    ///     Puts two listeners together into one listener that stops the two.
    /// </summary>
    /// <param name="l1">The first listener.</param>
    /// <param name="l2">The second listener.</param>
    /// <returns>A listener that stops the two given listeners.</returns>
    /// <remarks>
    ///     A convenience over <c>fromList</c> for two listeners.
    /// </remarks>
    let append l1 l2 = fromList (l1 :: l2 :: [])

/// <summary>
///     Stopping and combining weak listeners, preserving the <c>IWeakListener</c> type.
/// </summary>
/// <remarks>
///     A weak listener, the handle from <c>Stream.listen</c>, does not keep the stream that it
///     monitors in memory. The listener stops when a GC collects that stream, thus this is the
///     correct selection where there is no clear moment to stop the listener. Keep the handle in a
///     field of the object that listens, and the two go out of memory together.
///
///     Everything here also works through <c>Listener</c>, which is the same operation typed more
///     loosely.
/// </remarks>
module WeakListener =
    open System.Runtime.CompilerServices

    /// <summary>
    ///     Stops listening.
    /// </summary>
    /// <param name="listener">The listener to stop.</param>
    /// <remarks>
    ///     A second call is safe. Each call after the first does nothing.
    /// </remarks>
    let unlisten (listener: IWeakListener) = listener.Unlisten()
    /// <summary>
    ///     Gets a view of a weak listener which does not keep the stream it observes alive.
    /// </summary>
    /// <param name="listener">The listener to give a weak reference to.</param>
    /// <returns>A handle that stops the listener.</returns>
    /// <remarks>
    ///     A weak listener does not root the streams that it monitors. This is here to agree with
    ///     <c>Listener</c>, and not because it changes what stays in memory.
    /// </remarks>
    let getListenerWithWeakReference (listener: IWeakListener) = listener.GetListenerWithWeakReference()

    /// <summary>
    ///     A weak listener which is not listening to anything, and whose <c>unlisten</c> does nothing.
    /// </summary>
    let empty = ListenerInternal.EmptyWeakImpl

    /// <summary>
    ///     Combines a list of weak listeners into one weak listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>A weak listener whose <c>unlisten</c> stops each listener in the list.</returns>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let fromList listeners =
        ListenerInternal.CreateWeakCompositeImpl listeners

    /// <summary>
    ///     Combines a sequence of weak listeners into one weak listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>A weak listener whose <c>unlisten</c> stops each listener in the sequence.</returns>
    /// <remarks>
    ///     This code enumerates the sequence one time, immediately.
    /// </remarks>
    let fromSeq listeners = List.ofSeq listeners |> fromList

    /// <summary>
    ///     Puts two weak listeners together into one weak listener that stops the two.
    /// </summary>
    /// <param name="l1">The first listener.</param>
    /// <param name="l2">The second listener.</param>
    /// <returns>A weak listener that stops the two given listeners.</returns>
    let append l1 l2 = fromList (l1 :: l2 :: [])

/// <summary>
///     Stopping and combining strong listeners, preserving the <c>IStrongListener</c> type.
/// </summary>
/// <remarks>
///     A strong listener is the handle from <c>Stream.listenStrong</c>. It keeps the stream that it
///     monitors in memory for as long as code can get to the handle. It is <c>IDisposable</c>, thus
///     <c>use</c> can stop it.
///
///     Everything here also works through <c>Listener</c>, which is the same operation typed more
///     loosely.
/// </remarks>
module StrongListener =
    open System.Runtime.CompilerServices

    /// <summary>
    ///     Stops listening.
    /// </summary>
    /// <param name="listener">The listener to stop.</param>
    /// <remarks>
    ///     A second call is safe and does nothing. A disposal of the listener has the same result.
    /// </remarks>
    let unlisten (listener: IStrongListener) = listener.Unlisten()
    /// <summary>
    ///     Gets a view of a strong listener which does not keep the stream it observes alive.
    /// </summary>
    /// <param name="listener">The listener to give a weak reference to.</param>
    /// <returns>
    ///     A handle that stops the listener. It does not by itself prevent
    ///     the observed stream from being garbage collected.
    /// </returns>
    /// <remarks>
    ///     The initial strong listener continues to root the stream. This gives a second handle that
    ///     does not root it.
    /// </remarks>
    let getListenerWithWeakReference (listener: IStrongListener) = listener.GetListenerWithWeakReference()

    /// <summary>
    ///     A strong listener which is not listening to anything, and whose <c>unlisten</c> does nothing.
    /// </summary>
    let empty = ListenerInternal.EmptyStrongImpl

    /// <summary>
    ///     Combines a list of strong listeners into one strong listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>
    ///     A strong listener whose <c>unlisten</c> stops each listener in the list. A disposal does the
    ///     same.
    /// </returns>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let fromList listeners =
        ListenerInternal.CreateStrongCompositeImpl listeners

    /// <summary>
    ///     Combines a sequence of strong listeners into one strong listener which stops all of them.
    /// </summary>
    /// <param name="listeners">The listeners to make into one listener.</param>
    /// <returns>A strong listener whose <c>unlisten</c> stops each listener in the sequence.</returns>
    /// <remarks>
    ///     This code enumerates the sequence one time, immediately.
    /// </remarks>
    let fromSeq listeners = List.ofSeq listeners |> fromList

    /// <summary>
    ///     Puts two strong listeners together into one strong listener that stops the two.
    /// </summary>
    /// <param name="l1">The first listener.</param>
    /// <param name="l2">The second listener.</param>
    /// <returns>A strong listener that stops the two given listeners.</returns>
    let append l1 l2 = fromList (l1 :: l2 :: [])

/// <summary>
///     Pointing a mutable listener at a listener, and clearing or stopping it.
/// </summary>
/// <remarks>
///     A mutable listener is one handle that can point to a different listener. Use it in an object
///     with a long life that listens to a sequence of sources with short lives. It is one field
///     with one life and a target that changes.
/// </remarks>
module MutableListener =
    open System.Runtime.CompilerServices

    /// <summary>
    ///     Points a mutable listener at a listener, stopping whatever it pointed at before.
    /// </summary>
    /// <param name="l">The listener to become the target.</param>
    /// <param name="m">The mutable listener.</param>
    /// <remarks>
    ///     Ownership passes to the mutable listener: stopping it stops <paramref name="l" /> too, as
    ///     does the next <c>setListener</c> or <c>clearListener</c>.
    /// </remarks>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let setListener l (m: MutableListener) = m.SetListenerImpl l

    /// <summary>
    ///     Stops whatever a mutable listener currently points at, leaving it pointing at nothing.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <remarks>
    ///     The mutable listener stays usable and can move to a different listener after this. To
    ///     finish with it fully, use <c>unlisten</c>.
    /// </remarks>
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let clearListener (m: MutableListener) = m.ClearListenerImpl()

    /// <summary>
    ///     Stops listening, stopping whatever the mutable listener currently points at.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <remarks>
    ///     A second call is safe. Each call after the first does nothing.
    /// </remarks>
    let unlisten (m: MutableListener) = Listener.unlisten m
    /// <summary>
    ///     Gets a view of a mutable listener which does not keep the streams it observes alive.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <returns>
    ///     A handle that stops the listener. It does not by itself prevent
    ///     the observed streams from being garbage collected.
    /// </returns>
    let getListenerWithWeakReference (m: MutableListener) = Listener.getListenerWithWeakReference m
