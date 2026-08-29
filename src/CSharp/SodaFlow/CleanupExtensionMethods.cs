using System.Runtime.CompilerServices;

namespace SodaFlow
{
    /// <summary>
    ///     The operations available on a <see cref="Cleanup" />.
    /// </summary>
    public static class CleanupExtensionMethods
    {
        /// <summary>
        ///     Force the cleanup to happen now rather than waiting for this object to be garbage collected.
        /// </summary>
        /// <param name="c">The cleanup object.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void CleanupNow(this Cleanup c) => c.CleanupNowImpl();
    }
}