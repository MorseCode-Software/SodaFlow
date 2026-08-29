/// <summary>
///     Running a <c>Cleanup</c> at a known moment rather than waiting for the collector.
/// </summary>
module SodaFlow.Cleanup

open System.Runtime.CompilerServices

/// <summary>
///     Runs the cleanup action now, rather than waiting for the object to be garbage collected.
/// </summary>
/// <param name="cleanup">The cleanup object.</param>
/// <remarks>
///     A <c>Cleanup</c> otherwise runs its action from a finalizer, at a time the collector
///     chooses. Use this where the moment matters.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let cleanupNow (cleanup : Cleanup) = cleanup.CleanupNowImpl ()