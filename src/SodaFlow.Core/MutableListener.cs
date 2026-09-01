namespace SodaFlow
{
    /// <summary>
    ///     A listener whose target can be replaced while the handle itself stays the same.
    /// </summary>
    /// <remarks>
    ///     Intended for a long-lived object subscribing to a succession of short-lived sources: one
    ///     field, one lifetime, a changing target. Point it at a listener with
    ///     <c>SetListener</c>, release the current one with
    ///     <c>ClearListener</c>, and stop listening entirely
    ///     with <c>Unlisten</c>.
    /// </remarks>
    public class MutableListener : IListener
    {
        private readonly WeakMutableListener weakMutableListener = new WeakMutableListener();
        private IListener listener;

        internal void SetListenerImpl(IListener listener)
        {
            this.listener = listener;
            this.weakMutableListener.WeakListener = listener?.GetListenerWithWeakReference();
        }

        internal void ClearListenerImpl()
        {
            this.listener = null;
            this.weakMutableListener.WeakListener = null;
        }

        void IListener.Unlisten() => this.listener?.Unlisten();
        IListenerWithWeakReference IListener.GetListenerWithWeakReference() => this.weakMutableListener;

        private class WeakMutableListener : IListenerWithWeakReference
        {
            public IListenerWithWeakReference WeakListener;

            void IListenerWithWeakReference.Unlisten() => this.WeakListener?.Unlisten();
        }
    }
}
