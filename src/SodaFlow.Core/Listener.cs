using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A listener for stream firings. Call <see cref="Unlisten" /> to stop the
///     listener.
/// </summary>
[PublicAPI]
public interface IListener
{
    /// <summary>
    ///     Stops listening.
    /// </summary>
    /// <remarks>
    ///     You can call this more than one time. A subsequent call does nothing.
    /// </remarks>
    void Unlisten();
}

/// <summary>
///     A listener for stream firings. It listens until a caller calls
///     <see cref="IListener.Unlisten" />. It continues to listen after it goes out
///     of scope.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IStrongListener : IListener, IDisposable;

/// <summary>
///     A listener for stream firings. The garbage collector can remove it after it
///     goes out of scope. A call to <see cref="IListener.Unlisten" /> also stops
///     it.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IWeakListener : IListener;

// The weak view of a listener. It stops the listener, and it does not keep the stream alive. The
// registry entry of a stream holds the weak views of the listeners attached to the stream. Thus,
// the registry does not keep the stream alive. See StreamNode<T>.
internal interface IListenerWithWeakReference
{
    // Stops the listener. A subsequent call does nothing.
    void Unlisten();
}

// A listener that the library makes, which also gives its weak view. A stream attaches only this
// type. Thus, the compiler makes sure that each attached listener has a weak view. Before, the weak
// view was a member of IListener, and a caller that wrote an IListener had to write it.
internal interface IListenerInternal : IListener
{
    IListenerWithWeakReference GetListenerWithWeakReference();
}

internal interface IWeakListenerInternal : IWeakListener, IListenerInternal;

// A one-shot listener that roots nothing until a call to Root. The F# listenOnceAsync uses it. The
// async holds it until the async runs, and the async roots it when it runs. Thus, an async that does
// not run does not keep the listener on the stream.
internal interface IRootableListener : IWeakListenerInternal
{
    // Keeps the listener alive until it fires or stops. After it fires or stops, this does nothing.
    void Root();
}

internal static class ListenerInternal
{
    internal static readonly IListener EmptyImpl = EmptyListener.Instance;
    internal static readonly IWeakListener EmptyWeakImpl = EmptyListener.Instance;
    internal static readonly IStrongListener EmptyStrongImpl = EmptyListener.Instance;

    internal static IListenerInternal CreateFromAction(Action unlisten) => new ActionListener(unlisten);

    internal static IListenerInternal CreateCompositeImpl<T>(IReadOnlyList<T> listeners)
        where T : IListener =>
        new CompositeListener<T>(listeners);

    internal static IWeakListener CreateWeakCompositeImpl(IReadOnlyList<IWeakListener> listeners) =>
        new CompositeWeakListener(listeners);

    internal static IStrongListener CreateStrongCompositeImpl(IReadOnlyList<IStrongListener> listeners) =>
        new CompositeStrongListener(listeners);

    // A listener that a caller wrote has no weak view of its own. Its view holds it, and calls it.
    // Only a composite listener can hold such a listener.
    private static IListenerWithWeakReference GetListenerWithWeakReference(IListener listener) =>
        listener is IListenerInternal l ? l.GetListenerWithWeakReference() : new ExternalListenerView(listener);

    private sealed class EmptyListener : IStrongListener, IWeakListenerInternal, IListenerWithWeakReference
    {
        public static readonly EmptyListener Instance = new();

        private EmptyListener()
        {
        }

        public void Unlisten()
        {
        }

        public IListenerWithWeakReference GetListenerWithWeakReference() => this;

        public void Dispose()
        {
        }
    }

    /// <summary>
    ///     A listener that runs the given action when a caller disposes it.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ActionListener : IListenerInternal, IListenerWithWeakReference
    {
        private readonly Action unlisten;

        /// <summary>
        ///     Creates a listener that runs the given action when a caller disposes it.
        /// </summary>
        /// <param name="unlisten">The action that runs when this listener must stop.</param>
        internal ActionListener(Action unlisten) => this.unlisten = unlisten;

        public void Unlisten() => this.unlisten();
        public IListenerWithWeakReference GetListenerWithWeakReference() => this;
    }

    private class CompositeListener<T>(IReadOnlyList<T> listeners) : IListenerInternal, IListenerWithWeakReference
        where T : IListener
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly IReadOnlyList<T> listeners = listeners;

        public void Unlisten()
        {
            foreach (T l in this.listeners)
            {
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract - Protect
                // against caller adding null.
                l?.Unlisten();
            }
        }

        public IListenerWithWeakReference GetListenerWithWeakReference() =>
            new CompositeWeakListenerWithWeakReference(
                [.. this.listeners.Select(static l => ListenerInternal.GetListenerWithWeakReference(l))]);

        private sealed class CompositeWeakListenerWithWeakReference(
            IReadOnlyList<IListenerWithWeakReference> weakListeners) : IListenerWithWeakReference
        {
            // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
            // captured into a mutable variable.
            private readonly IReadOnlyList<IListenerWithWeakReference> weakListeners = weakListeners;

            public void Unlisten()
            {
                foreach (IListenerWithWeakReference l in this.weakListeners)
                {
                    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract - Protect
                    // against caller adding null.
                    l?.Unlisten();
                }
            }
        }
    }

    private sealed class ExternalListenerView(in IListener listener) : IListenerWithWeakReference
    {
        private readonly IListener listener = listener;

        public void Unlisten() => this.listener.Unlisten();
    }

    private sealed class CompositeStrongListener(IReadOnlyList<IStrongListener> listeners)
        : CompositeListener<IStrongListener>(listeners), IStrongListener
    {
        public void Dispose() => this.Unlisten();
    }

    private sealed class CompositeWeakListener(IReadOnlyList<IWeakListener> listeners)
        : CompositeListener<IWeakListener>(listeners), IWeakListener;
}
