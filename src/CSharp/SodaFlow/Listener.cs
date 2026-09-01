using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SodaFlow
{
    /// <summary>
    ///     Empty listeners, and combinators for treating several listeners as one.
    /// </summary>
    /// <remarks>
    ///     The composites returned here unlisten every listener they were built from, so a graph fragment
    ///     with several subscriptions can be torn down through a single handle.
    /// </remarks>
    public static class Listener
    {
        /// <summary>
        ///     A listener which is not listening to anything, and whose
        ///     <see cref="IListener.Unlisten" /> does nothing.
        /// </summary>
        /// <remarks>
        ///     Useful as the identity for <see cref="Append(IListener, IListener)" />, and as the
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
        ///     Combines several listeners into one which unlistens all of them.
        /// </summary>
        /// <param name="listeners">The listeners to combine.</param>
        /// <returns>
        ///     A listener whose <see cref="IListener.Unlisten" /> unlistens every listener in
        ///     <paramref name="listeners" />.
        /// </returns>
        /// <remarks>
        ///     The list is captured when the composite is created; later changes to it are not seen.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IListener CreateComposite(IReadOnlyList<IListener> listeners) =>
            ListenerInternal.CreateCompositeImpl(listeners);

        /// <summary>
        ///     Combines several weak listeners into one weak listener which unlistens all of them.
        /// </summary>
        /// <param name="listeners">The listeners to combine.</param>
        /// <returns>
        ///     An <see cref="IWeakListener" /> whose <see cref="IListener.Unlisten" /> unlistens every
        ///     listener in <paramref name="listeners" />.
        /// </returns>
        /// <remarks>
        ///     Like the listeners it combines, the result does not keep the observed streams alive.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IWeakListener CreateWeakComposite(IReadOnlyList<IWeakListener> listeners) =>
            ListenerInternal.CreateWeakCompositeImpl(listeners);

        /// <summary>
        ///     Combines several strong listeners into one strong listener which unlistens all of them.
        /// </summary>
        /// <param name="listeners">The listeners to combine.</param>
        /// <returns>
        ///     An <see cref="IStrongListener" /> whose <see cref="IListener.Unlisten" /> unlistens every
        ///     listener in <paramref name="listeners" />, and which may be disposed to the same effect.
        /// </returns>
        /// <remarks>
        ///     Like the listeners it combines, the result keeps the observed streams alive until it is
        ///     unlistened or disposed.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IStrongListener CreateStrongComposite(IReadOnlyList<IStrongListener> listeners) =>
            ListenerInternal.CreateStrongCompositeImpl(listeners);

        /// <summary>
        ///     Combines two listeners into one which unlistens both.
        /// </summary>
        /// <param name="listener1">The first listener.</param>
        /// <param name="listener2">The second listener.</param>
        /// <returns>A listener which unlistens both of the given listeners.</returns>
        /// <remarks>
        ///     A convenience over <see cref="CreateComposite" /> for the two-listener case.
        /// </remarks>
        public static IListener Append(IListener listener1, IListener listener2) =>
            CreateComposite(new[] { listener1, listener2 });

        /// <summary>
        ///     Combines two weak listeners into one weak listener which unlistens both.
        /// </summary>
        /// <param name="listener1">The first listener.</param>
        /// <param name="listener2">The second listener.</param>
        /// <returns>An <see cref="IWeakListener" /> which unlistens both of the given listeners.</returns>
        /// <remarks>
        ///     Named differently from <see cref="Append(IListener, IListener)" /> because
        ///     <see cref="IWeakListener" /> and <see cref="IListener" /> would otherwise give two
        ///     overloads that a pair of weak listeners could match either of.
        /// </remarks>
        public static IWeakListener AppendWeak(IWeakListener listener1, IWeakListener listener2) =>
            CreateWeakComposite(new[] { listener1, listener2 });

        /// <summary>
        ///     Combines two strong listeners into one strong listener which unlistens both.
        /// </summary>
        /// <param name="listener1">The first listener.</param>
        /// <param name="listener2">The second listener.</param>
        /// <returns>
        ///     An <see cref="IStrongListener" /> which unlistens both of the given listeners, and which
        ///     may be disposed to the same effect.
        /// </returns>
        public static IStrongListener Append(IStrongListener listener1, IStrongListener listener2) =>
            CreateStrongComposite(new[] { listener1, listener2 });
    }
}