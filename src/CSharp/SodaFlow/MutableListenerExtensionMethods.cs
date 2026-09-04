using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="MutableListener" />.
/// </summary>
[PublicAPI]
public static class MutableListenerExtensionMethods
{
    /// <param name="m">The mutable listener.</param>
    extension(MutableListener m)
    {
        /// <summary>
        ///     Points the mutable listener at <paramref name="listener" />, unlistening whatever it was
        ///     pointed at before.
        /// </summary>
        /// <param name="listener">The listener to take over.</param>
        /// <remarks>
        ///     Ownership passes to <paramref name="m" />: unlistening it unlistens
        ///     <paramref name="listener" /> too, as does the next call to this method or to
        ///     <see cref="ClearListener" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetListener(IListener listener) => m.SetListenerImpl(listener);

        /// <summary>
        ///     Unlistens whatever the mutable listener is currently pointed at, leaving it pointed at
        ///     nothing.
        /// </summary>
        /// <remarks>
        ///     The mutable listener remains usable afterward and can be pointed at another listener
        ///     with <see cref="SetListener" />. To finish with it entirely, call <see cref="Unlisten" />
        ///     instead.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ClearListener() => m.ClearListenerImpl();

        /// <summary>
        ///     Stops listening, unlistening whatever the mutable listener is currently pointed at.
        /// </summary>
        /// <remarks>
        ///     Safe to call more than once; subsequent calls do nothing.
        /// </remarks>
        public void Unlisten()
        {
            IListener l = m;
            l.Unlisten();
        }

        /// <summary>
        ///     Gets a view of this mutable listener which does not keep the streams it listens to alive.
        /// </summary>
        /// <returns>
        ///     A listener which can still be used to
        ///     <see cref="IListenerWithWeakReference.Unlisten" />, but which will not by itself prevent
        ///     the observed streams from being garbage collected.
        /// </returns>
        public IListenerWithWeakReference GetListenerWithWeakReference()
        {
            IListener l = m;
            return l.GetListenerWithWeakReference();
        }
    }
}
