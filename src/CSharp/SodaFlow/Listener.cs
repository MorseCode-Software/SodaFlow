using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Empty listeners, and combinators for treating some listeners as one.
/// </summary>
/// <remarks>
///     Each composite here stops each listener that made it. Thus, one handle releases a part of
///     the graph with some subscriptions.
/// </remarks>
[PublicAPI]
public static class Listener
{
    /// <summary>
    ///     A listener which is not listening to anything, and whose
    ///     <see cref="IListener.Unlisten" /> does nothing.
    /// </summary>
    /// <remarks>
    ///     Useful as the identity for <see cref="Append" />, and as the
    ///     result of a code path which has nothing to unsubscribe.
    /// </remarks>
    public static readonly IListener Empty = ListenerInternal.EmptyImpl;

    /// <summary>
    ///     An <see cref="IWeakListener" /> which is not listening to anything, and whose
    ///     <see cref="IListener.Unlisten" /> does nothing.
    /// </summary>
    public static readonly IWeakListener EmptyWeak = ListenerInternal.EmptyWeakImpl;

    /// <summary>
    ///     An <see cref="IStrongListener" /> which is not listening to anything, and whose
    ///     <see cref="IListener.Unlisten" /> does nothing.
    /// </summary>
    public static readonly IStrongListener EmptyStrong = ListenerInternal.EmptyStrongImpl;

    /// <summary>
    ///     Combines some listeners into one which unlistens all of them.
    /// </summary>
    /// <param name="listeners">The listeners to put together.</param>
    /// <returns>
    ///     A listener whose <see cref="IListener.Unlisten" /> unlistens each listener in
    ///     <paramref name="listeners" />.
    /// </returns>
    /// <remarks>
    ///     This code reads the list at the construction of the composite. A subsequent change to the list has
    ///     no effect.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IListener CreateComposite(IReadOnlyList<IListener> listeners) =>
        ListenerInternal.CreateCompositeImpl(listeners);

    /// <summary>
    ///     Combines some weak listeners into one weak listener which unlistens all of them.
    /// </summary>
    /// <param name="listeners">The listeners to put together.</param>
    /// <returns>
    ///     An <see cref="IWeakListener" /> whose <see cref="IListener.Unlisten" /> unlistens each
    ///     listener in <paramref name="listeners" />.
    /// </returns>
    /// <remarks>
    ///     Like the listeners it combines, the result does not keep the observed streams alive.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IWeakListener CreateWeakComposite(IReadOnlyList<IWeakListener> listeners) =>
        ListenerInternal.CreateWeakCompositeImpl(listeners);

    /// <summary>
    ///     Combines some strong listeners into one strong listener which unlistens all of them.
    /// </summary>
    /// <param name="listeners">The listeners to put together.</param>
    /// <returns>
    ///     An <see cref="IStrongListener" /> whose <see cref="IListener.Unlisten" /> unlistens each
    ///     listener in <paramref name="listeners" />, and a disposal does the same.
    /// </returns>
    /// <remarks>
    ///     As the listeners in it do, the result keeps the monitored streams in memory until a call
    ///     stops it, or a disposal stops it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IStrongListener CreateStrongComposite(IReadOnlyList<IStrongListener> listeners) =>
        ListenerInternal.CreateStrongCompositeImpl(listeners);

    /// <summary>
    ///     Combines two listeners into one which unlistens the two.
    /// </summary>
    /// <param name="listener1">The first listener.</param>
    /// <param name="listener2">The second listener.</param>
    /// <returns>A listener which unlistens the two given listeners.</returns>
    /// <remarks>
    ///     A convenience over <see cref="CreateComposite" /> for two listeners.
    /// </remarks>
    public static IListener Append(IListener listener1, IListener listener2) => CreateComposite([listener1, listener2]);

    /// <summary>
    ///     Combines two weak listeners into one weak listener which unlistens the two.
    /// </summary>
    /// <param name="listener1">The first listener.</param>
    /// <param name="listener2">The second listener.</param>
    /// <returns>An <see cref="IWeakListener" /> which unlistens the two given listeners.</returns>
    /// <remarks>
    ///     This and <see cref="AppendStrong" /> have their own names, as
    ///     <see cref="CreateWeakComposite" /> and <see cref="CreateStrongComposite" /> do. As
    ///     overloads of <see cref="Append" />, a type that is weak and strong at once matches each
    ///     of them, and the call does not compile.
    /// </remarks>
    public static IWeakListener AppendWeak(IWeakListener listener1, IWeakListener listener2) =>
        CreateWeakComposite([listener1, listener2]);

    /// <summary>
    ///     Combines two strong listeners into one strong listener which unlistens the two.
    /// </summary>
    /// <param name="listener1">The first listener.</param>
    /// <param name="listener2">The second listener.</param>
    /// <returns>
    ///     An <see cref="IStrongListener" /> which unlistens the two given listeners, and a
    ///     disposal does the same.
    /// </returns>
    /// <remarks>
    ///     See <see cref="AppendWeak" /> for the reason this has its own name.
    /// </remarks>
    public static IStrongListener AppendStrong(IStrongListener listener1, IStrongListener listener2) =>
        CreateStrongComposite([listener1, listener2]);
}
