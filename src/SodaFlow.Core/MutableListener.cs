using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A listener whose target can be replaced while the handle itself stays the same.
/// </summary>
/// <remarks>
///     Use this for an object with a long life that listens to a sequence of sources with short
///     lives. It is one field with one life and a target that changes. To give it a listener,
///     call <c>SetListener</c>. To release the current listener, call <c>ClearListener</c>. To
///     stop it fully, call <c>Unlisten</c>.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class MutableListener : IListener
{
    private readonly WeakMutableListener weakMutableListener = new();
    private IListener? listener;

    void IListener.Unlisten() => this.listener?.Unlisten();
    IListenerWithWeakReference IListener.GetListenerWithWeakReference() => this.weakMutableListener;

    internal void SetListenerImpl(IListener listener)
    {
        this.listener = listener;
        this.weakMutableListener.WeakListener = listener.GetListenerWithWeakReference();
    }

    internal void ClearListenerImpl()
    {
        this.listener = null;
        this.weakMutableListener.WeakListener = null;
    }

    private class WeakMutableListener : IListenerWithWeakReference
    {
        public IListenerWithWeakReference? WeakListener;

        void IListenerWithWeakReference.Unlisten() => this.WeakListener?.Unlisten();
    }
}
