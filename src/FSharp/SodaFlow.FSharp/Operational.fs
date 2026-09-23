/// <summary>
///     Operations that go below the FRP abstraction, to the transactions.
/// </summary>
/// <remarks>
///     These give the mechanism that sends the updates, and not the meaning of the updates. Thus,
///     the denotational semantics of the other parts of the library do not apply to a graph that
///     uses them. Use them to make new primitives, and to connect to other code. Use them only when
///     nothing in <c>Stream</c>, <c>Cell</c> or <c>Behavior</c> is sufficient.
/// </remarks>
module SodaFlow.Operational

open System.Runtime.CompilerServices

/// <summary>
///     Gets a stream firing the new value of a behavior each time it changes.
/// </summary>
/// <param name="behavior">The behavior to monitor.</param>
/// <returns>A stream which fires the updated value, in the transaction the update happened in.</returns>
/// <remarks>
///     Does not fire for the initial value - only for changes. Use <c>value</c> to get the
///     initial value as a firing too.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let updates behavior =
    OperationalInternal.UpdatesImpl behavior

/// <summary>
///     Gets a stream firing the current value of the behavior immediately, and its new value on each change.
/// </summary>
/// <param name="behavior">The behavior to monitor.</param>
/// <returns>
///     A stream which fires the current value in the transaction of this call, and then the
///     updated value on each change.
/// </returns>
/// <remarks>
///     The first firing occurs in the transaction of this call. Thus, a caller must call this in
///     <c>Transaction.run</c> for a listener to see that firing. A listener that attaches after
///     that, in a subsequent transaction, does not get it.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let value behavior = OperationalInternal.ValueImpl behavior

/// <summary>
///     Gets a stream which fires each element of a fired collection in its own transaction.
/// </summary>
/// <param name="stream">The stream of collections to split.</param>
/// <returns>A stream firing the elements one at a time, each one in its own subsequent transaction.</returns>
/// <remarks>
///     The result defers the firings. They occur in the transactions after the transaction of the
///     collection, in the sequence that the collection gives them.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let split (stream: Stream<#seq<_>>) = OperationalInternal.SplitImpl stream

/// <summary>
///     Gets a stream which fires each value again in a subsequent transaction.
/// </summary>
/// <param name="stream">The stream to defer.</param>
/// <returns>A stream firing the same values, each in a transaction after the one it arrived in.</returns>
/// <remarks>
///     The equivalent of <c>split</c> for a single value rather than a collection.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let defer stream = OperationalInternal.DeferImpl stream
