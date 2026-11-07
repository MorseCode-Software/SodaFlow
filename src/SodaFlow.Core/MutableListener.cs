using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A listener whose target can be replaced while the handle itself stays the
///     same.
/// </summary>
/// <remarks>
///     Use this for an object with a long life that listens to a sequence of
///     sources with short lives. It is one field with one life and a target that
///     changes. To give it a listener, call <c>SetListener</c>. To release the
///     current listener, call <c>ClearListener</c>. To stop it fully, call
///     <c>Unlisten</c>.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class MutableListener : IListenerInternal
{
    private readonly WeakMutableListener weakMutableListener = new();
    private IListener? listener;

    void IListener.Unlisten() => this.listener?.Unlisten();
    IListenerWithWeakReference IListenerInternal.GetListenerWithWeakReference() => this.weakMutableListener;

    // The previous target stops after this code sets the new target. Thus, a reader of this
    // listener never finds it with no target between the two. A set of the target that this
    // listener holds now stops nothing. Otherwise, a second set of one listener stops it with no
    // message.
    internal void SetListenerImpl(IListener listener)
    {
        IListener? previous = this.listener;

        if (ReferenceEquals(objA: previous, objB: listener))
        {
            return;
        }

        this.listener = listener;
        this.weakMutableListener.WeakListener = ListenerInternal.GetListenerWithWeakReference(listener);
        previous?.Unlisten();
    }

    internal void ClearListenerImpl()
    {
        IListener? previous = this.listener;
        this.listener = null;
        this.weakMutableListener.WeakListener = null;
        previous?.Unlisten();
    }

    private class WeakMutableListener : IListenerWithWeakReference
    {
        public IListenerWithWeakReference? WeakListener;

        void IListenerWithWeakReference.Unlisten() => this.WeakListener?.Unlisten();
    }
}
