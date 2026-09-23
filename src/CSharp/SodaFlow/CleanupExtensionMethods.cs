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
    ///     Forces the cleanup to occur now, and does not wait for a GC to collect this object.
    /// </summary>
    /// <param name="c">The cleanup object.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CleanupNow(this Cleanup c) => c.CleanupNowImpl();
}
