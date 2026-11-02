using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="Cleanup" />.
/// </summary>
[PublicAPI]
public static class CleanupExtensionMethods
{
    /// <summary>
    ///     Runs the cleanup action now, on this thread, and does not wait for a GC to
    ///     collect this object.
    /// </summary>
    /// <param name="c">The cleanup object.</param>
    /// <remarks>
    ///     The action runs one time. A second call does nothing, and the collection
    ///     of the object after this call runs nothing.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CleanupNow(this Cleanup c) => c.CleanupNowImpl();
}
