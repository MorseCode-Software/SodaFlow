using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A listener for stream firings. It listens while a caller holds it. The
///     garbage collector can remove it after no caller holds it. A call to
///     <see cref="Unlisten" /> or to <see cref="IDisposable.Dispose" /> stops it.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage - The summary says what a listener is, and the documentation of
// IDisposable does not.
public interface IListener : IDisposable
{
    /// <summary>
    ///     Stops listening.
    /// </summary>
    /// <remarks>
    ///     You can call this more than one time. A subsequent call does nothing.
    /// </remarks>
    void Unlisten();
}

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

// A one-shot listener that roots nothing until a call to Root. ListenOnceAsync uses it in each
// language, and no other code roots a listener. The C# call roots it immediately. The F# async holds
// it until the async runs, and the async roots it when it runs. Thus, an async that does not run
// does not keep the listener on the stream.
internal interface IRootableListener : IListenerInternal
{
    // Keeps the listener alive until it fires or stops. After it fires or stops, this does nothing.
    void Root();
}

internal static class ListenerInternal
{
    internal static readonly IListener EmptyImpl = EmptyListener.Instance;

    internal static IListenerInternal CreateCompositeImpl<T>(IReadOnlyList<T> listeners)
        where T : IListener =>
        new CompositeListener<T>(listeners);

    // A listener that a caller wrote has no weak view of its own. Its view holds it, and calls it.
    // Only a composite listener can hold such a listener.
    private static IListenerWithWeakReference GetListenerWithWeakReference(IListener listener) =>
        listener is IListenerInternal l ? l.GetListenerWithWeakReference() : new ExternalListenerView(listener);

    private sealed class EmptyListener : IListenerInternal, IListenerWithWeakReference
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

    private sealed class CompositeListener<T>(IReadOnlyList<T> listeners)
        : IListenerInternal, IListenerWithWeakReference
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

        public void Dispose() => this.Unlisten();

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
}
