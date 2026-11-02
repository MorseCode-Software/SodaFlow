/// <summary>
///     Runs a <c>Cleanup</c> at a known moment, and does not wait for the collector.
/// </summary>
module SodaFlow.Cleanup

open System.Runtime.CompilerServices

/// <summary>
///     Runs the cleanup action now, on this thread, and does not wait for a GC to collect the
///     object.
/// </summary>
/// <param name="cleanup">The cleanup object.</param>
/// <remarks>
///     The action runs one time. A second call does nothing, and the collection of the object
///     after this call runs nothing.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let cleanupNow (cleanup: Cleanup) = cleanup.CleanupNowImpl()
