using System.Runtime.CompilerServices;

namespace SodaFlow
{
    /// <summary>
    ///     The operations available on a <see cref="MutableListener" />.
    /// </summary>
    public static class MutableListenerExtensionMethods
    {
        /// <summary>
        ///     Points the mutable listener at <paramref name="listener" />, unlistening whatever it was
        ///     pointed at before.
        /// </summary>
        /// <param name="m">The mutable listener.</param>
        /// <param name="listener">The listener to take over.</param>
        /// <remarks>
        ///     Ownership passes to <paramref name="m" />: unlistening it unlistens
        ///     <paramref name="listener" /> too, as does the next call to this method or to
        ///     <see cref="ClearListener" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SetListener(this MutableListener m, IListener listener) => m.SetListenerImpl(listener);

        /// <summary>
        ///     Unlistens whatever the mutable listener is currently pointed at, leaving it pointed at
        ///     nothing.
        /// </summary>
        /// <param name="m">The mutable listener.</param>
        /// <remarks>
        ///     The mutable listener remains usable afterwards and can be pointed at another listener
        ///     with <see cref="SetListener" />. To finish with it entirely, call <see cref="Unlisten" />
        ///     instead.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ClearListener(this MutableListener m) => m.ClearListenerImpl();

        /// <summary>
        ///     Stops listening, unlistening whatever the mutable listener is currently pointed at.
        /// </summary>
        /// <param name="m">The mutable listener.</param>
        /// <remarks>
        ///     Safe to call more than once; subsequent calls do nothing.
        /// </remarks>
        public static void Unlisten(this MutableListener m)
        {
            IListener l = m;
            l.Unlisten();
        }

        /// <summary>
        ///     Gets a view of this mutable listener which does not keep the streams it listens to alive.
        /// </summary>
        /// <param name="m">The mutable listener.</param>
        /// <returns>
        ///     A listener which can still be used to
        ///     <see cref="IListenerWithWeakReference.Unlisten" />, but which will not by itself prevent
        ///     the observed streams from being garbage collected.
        /// </returns>
        public static IListenerWithWeakReference GetListenerWithWeakReference(this MutableListener m)
        {
            IListener l = m;
            return l.GetListenerWithWeakReference();
        }
    }
}