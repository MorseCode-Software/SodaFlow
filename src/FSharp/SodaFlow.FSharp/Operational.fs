/// <summary>
///     Operations which reach past the FRP abstraction to the transactions underneath.
/// </summary>
/// <remarks>
///     These expose how updates are actually delivered rather than what they mean, so a graph
///     built with them is no longer described by the denotational semantics the rest of the
///     library obeys. They exist for building new primitives and for interfacing with the outside
///     world; reach for them only when nothing in <c>Stream</c>, <c>Cell</c> or <c>Behavior</c>
///     will do.
/// </remarks>
module SodaFlow.Operational

open System.Runtime.CompilerServices

/// <summary>
///     Gets a stream firing the new value of a behavior each time it changes.
/// </summary>
/// <param name="behavior">The behavior to observe.</param>
/// <returns>A stream which fires the updated value, in the transaction the update happened in.</returns>
/// <remarks>
///     Does not fire for the initial value - only for changes. Use <c>value</c> to get the
///     initial value as a firing too.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let updates behavior = OperationalInternal.UpdatesImpl behavior

/// <summary>
///     Gets a stream firing the behavior's current value at once, and its new value on every change.
/// </summary>
/// <param name="behavior">The behavior to observe.</param>
/// <returns>
///     A stream which fires the current value in the transaction this is called in, and then the
///     updated value on every change.
/// </returns>
/// <remarks>
///     The immediate firing happens in the transaction this is called in, so this must be called
///     inside <c>Transaction.run</c> if that firing is to be observed - a listener attached
///     afterwards, in a later transaction, has already missed it.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let value behavior = OperationalInternal.ValueImpl behavior

/// <summary>
///     Gets a stream which fires each element of a fired collection in its own transaction.
/// </summary>
/// <param name="stream">The stream of collections to split.</param>
/// <returns>A stream firing the elements one at a time, each in a separate later transaction.</returns>
/// <remarks>
///     The firings are deferred: they happen in transactions after the one the collection fired
///     in, in the order the collection yields them.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let split (stream : Stream<#seq<_>>) = OperationalInternal.SplitImpl stream

/// <summary>
///     Gets a stream which re-fires each value in a later transaction.
/// </summary>
/// <param name="stream">The stream to defer.</param>
/// <returns>A stream firing the same values, each in a transaction after the one it arrived in.</returns>
/// <remarks>
///     The equivalent of <c>split</c> for a single value rather than a collection.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let defer stream = OperationalInternal.DeferImpl stream