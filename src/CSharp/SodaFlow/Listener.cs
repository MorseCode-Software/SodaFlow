using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Empty listeners, and combinators for treating some listeners as one.
/// </summary>
/// <remarks>
///     Each composite here keeps each listener that made it, and stops each of
///     them. Thus, one handle keeps some subscriptions, and releases them with a
///     part of the graph.
/// </remarks>
[PublicAPI]
public static class Listener
{
    /// <summary>
    ///     A listener which is not listening to anything, and whose
    ///     <see cref="IListener.Unlisten" /> does nothing.
    /// </summary>
    /// <remarks>
    ///     Useful as the identity for <see cref="Append" />, and as the result of a
    ///     code path which has nothing to unsubscribe.
    /// </remarks>
    public static readonly IListener Empty = ListenerInternal.EmptyImpl;

    /// <summary>
    ///     Combines some listeners into one which unlistens all of them.
    /// </summary>
    /// <param name="listeners">The listeners to put together.</param>
    /// <returns>
    ///     A listener whose <see cref="IListener.Unlisten" /> unlistens each listener
    ///     in <paramref name="listeners" />.
    /// </returns>
    /// <remarks>
    ///     This code reads the list at the construction of the composite. A subsequent
    ///     change to the list has no effect.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IListener CreateComposite(IReadOnlyList<IListener> listeners) =>
        ListenerInternal.CreateCompositeImpl(listeners);

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
}
