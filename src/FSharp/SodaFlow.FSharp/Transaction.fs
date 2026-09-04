/// <summary>
///     Running work inside a transaction, and hooking the transactions others run.
/// </summary>
/// <remarks>
///     Transactions are serialized process-wide: at most one runs at a time, however many
///     threads are involved, and a thread starting one blocks until any transaction running on
///     another thread has finished. That is what makes a transaction atomic with respect to every
///     other thread - no observer ever sees the graph half-updated - and it is why SodaFlow can be
///     used from several threads with no synchronization of your own.
///
///     The cost is that the lock is held for the whole transaction, including every listener
///     callback it fires and every <c>post</c> action it queues. While a callback runs, no other
///     thread can begin a transaction, so callbacks should return promptly; hand long-running or
///     blocking work to another thread rather than doing it inline. A callback that blocks waiting
///     on a thread which is itself trying to start a transaction will deadlock.
///
///     Nesting is free. Starting a transaction while one is already running on the same thread
///     joins it rather than taking the lock again, so the primitives that open transactions of
///     their own cost nothing extra inside <c>run</c>.
/// </remarks>
module SodaFlow.Transaction

open System
open System.Runtime.CompilerServices

/// <summary>
///     Returns whether a transaction is currently running on this thread.
/// </summary>
/// <returns><c>true</c> if there is a current transaction, and <c>false</c> otherwise.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let isActive () = TransactionInternal.IsActiveImpl()

/// <summary>
///     Runs a function inside a single transaction and returns its result.
/// </summary>
/// <param name="f">The function to run.</param>
/// <returns>Whatever <paramref name="f" /> returned.</returns>
/// <remarks>
///     Rarely needed for a single operation, since every primitive opens a transaction of its own
///     where it needs one. It is for making several operations atomic together.
///
///     Build the graph inside one of these so that no first firing is missed - particularly where
///     <c>Cell.values</c> is involved, which always fires immediately. It is also required for
///     <c>Stream.loop</c>, <c>Cell.loop</c> and <c>Behavior.loop</c>, which must be created and
///     closed within one transaction.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let run f = TransactionInternal.RunImpl(Func<_> f)

/// <summary>
///     Registers an action to run whenever a transaction starts.
/// </summary>
/// <param name="a">The action to run at the start of every transaction.</param>
/// <remarks>
///     The action may start transactions itself without the hooks running recursively. This exists
///     for implementing a timer system - it is how <c>SodaFlow.Time</c> delivers alarms - and is
///     rarely what application code wants.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let onStart a =
    TransactionInternal.OnStartImpl(Action a)

/// <summary>
///     Runs an action once the current transaction has closed, or immediately if none is running.
/// </summary>
/// <param name="a">The action to run.</param>
/// <remarks>
///     The action still runs under the transaction lock, while the transaction is closing, so it
///     is subject to the same guidance as a listener callback: return promptly.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let post a = TransactionInternal.PostImpl(Action a)
