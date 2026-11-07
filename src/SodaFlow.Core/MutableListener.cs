namespace SodaFlow;

/// <summary>
///     A listener whose target can change while the listener itself stays the
///     same.
/// </summary>
/// <remarks>
///     A switch holds the link to its current inner source in one. The output of
///     the switch attaches it one time, and each selection calls
///     <see cref="SetListener" />.
/// </remarks>
internal sealed class MutableListener : IListenerInternal
{
    private readonly WeakMutableListener weakMutableListener = new();
    private IListenerInternal? listener;

    public void Unlisten() => this.listener?.Unlisten();

    public IListenerWithWeakReference GetListenerWithWeakReference() => this.weakMutableListener;

    // The previous target stops after this code sets the new target. Thus, a reader of this
    // listener never finds it with no target between the two. A set of the target that this
    // listener holds now stops nothing. Otherwise, a second set of one listener stops it with no
    // message.
    internal void SetListener(IListenerInternal listener)
    {
        IListenerInternal? previous = this.listener;

        if (ReferenceEquals(objA: previous, objB: listener))
        {
            return;
        }

        this.listener = listener;
        this.weakMutableListener.WeakListener = listener.GetListenerWithWeakReference();
        previous?.Unlisten();
    }

    private sealed class WeakMutableListener : IListenerWithWeakReference
    {
        public IListenerWithWeakReference? WeakListener;

        void IListenerWithWeakReference.Unlisten() => this.WeakListener?.Unlisten();
    }
}
