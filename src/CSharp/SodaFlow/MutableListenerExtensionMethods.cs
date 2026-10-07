using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="MutableListener" />.
/// </summary>
[PublicAPI]
public static class MutableListenerExtensionMethods
{
    /// <summary>
    ///     Points the mutable listener at <paramref name="listener" />, and stops the
    ///     listener that it pointed at before.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <param name="listener">The listener to become the target.</param>
    /// <remarks>
    ///     Ownership passes to <paramref name="m" />: unlistening it unlistens
    ///     <paramref name="listener" /> too, as does the next call to this method or
    ///     to <see cref="ClearListener" />.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SetListener(this MutableListener m, IListener listener) => m.SetListenerImpl(listener);

    /// <summary>
    ///     Unlistens whatever the mutable listener is currently pointed at, leaving it
    ///     pointed at nothing.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <remarks>
    ///     The mutable listener stays usable after this, and can point to a different
    ///     listener with <see cref="SetListener" />. To finish with it fully, call
    ///     <see cref="Unlisten" />.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ClearListener(this MutableListener m) => m.ClearListenerImpl();

    /// <summary>
    ///     Stops listening, unlistening whatever the mutable listener is currently
    ///     pointed at.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <remarks>
    ///     A second call is safe. Each call after the first does nothing.
    /// </remarks>
    public static void Unlisten(this MutableListener m)
    {
        IListener l = m;
        l.Unlisten();
    }

    /// <summary>
    ///     Gets a view of this mutable listener which does not keep the streams it
    ///     listens to alive.
    /// </summary>
    /// <param name="m">The mutable listener.</param>
    /// <returns>
    ///     A listener that <see cref="IListenerWithWeakReference.Unlisten" /> can
    ///     stop. It does not by itself keep the monitored streams in memory.
    /// </returns>
    public static IListenerWithWeakReference GetListenerWithWeakReference(this MutableListener m)
    {
        IListener l = m;
        return l.GetListenerWithWeakReference();
    }
}
