namespace SodaFlow

/// <summary>
///     Stopping listeners, and combining some of them into one.
/// </summary>
/// <remarks>
///     A listener is the handle returned by <c>Stream.listen</c>, <c>Stream.listenOnce</c>, and
///     <c>Cell.listen</c>. It listens while the caller keeps it, and it keeps the stream that it
///     monitors in memory. Keep the handle in a field of the object that listens, and the two go out
///     of memory together. It is <c>IDisposable</c>, thus <c>use</c> can keep it and stop it.
/// </remarks>
module Listener =
    open System.Runtime.CompilerServices

    /// <summary>
    ///     Stops listening.
    /// </summary>
    /// <param name="listener">The listener to stop.</param>
    /// <remarks>
    ///     A second call is safe. Each call after the first does nothing.
    /// </remarks>
    let unlisten (listener: IListener) = listener.Unlisten()

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
    let fromList (listeners: #IListener list) : IListener =
        ListenerInternal.CreateCompositeImpl listeners :> IListener

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
    ///     Puts two listeners together into one listener that stops the two.
    /// </summary>
    /// <param name="l1">The first listener.</param>
    /// <param name="l2">The second listener.</param>
    /// <returns>A listener that stops the two given listeners.</returns>
    /// <remarks>
    ///     A convenience over <c>fromList</c> for two listeners.
    /// </remarks>
    let append l1 l2 = fromList (l1 :: l2 :: [])
