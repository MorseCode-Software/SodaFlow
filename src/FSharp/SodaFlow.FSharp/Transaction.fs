/// <summary>
///     Runs work in a transaction, and connects to the transactions of other code.
/// </summary>
/// <remarks>
///     The process runs only one transaction at a time, on all of its threads. A thread that starts
///     a transaction waits until the transaction on a different thread ends. Thus, a transaction is
///     atomic for each other thread, and no code sees the graph in the middle of an update. This is
///     why code on more than one thread can use SodaFlow with no synchronization of its own.
///
///     The cost is that the transaction keeps the lock for all of its operation. That includes each
///     listener callback that it fires and each <c>post</c> action that it queues. While a callback
///     runs, no other thread can start a transaction. Thus, a callback must return quickly.
///     Give work that takes a long time, and work that blocks, to a different thread. A callback
///     that waits on a thread that tries to start a transaction causes a deadlock.
///
///     A nested transaction has no cost. A transaction that starts while one is open on the same
///     thread becomes part of the open transaction, and does not get the lock again. Thus, the
///     primitives that open their own transactions add no cost in <c>run</c>.
/// </remarks>
module SodaFlow.Transaction

open System
open System.Runtime.CompilerServices

/// <summary>
///     Gives true when a transaction is open on this thread.
/// </summary>
/// <returns><c>true</c> if there is a current transaction, and <c>false</c> otherwise.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let isActive () =
    TransactionInternal.HasCurrentTransaction()

/// <summary>
///     Runs a function in a single transaction and returns its result.
/// </summary>
/// <param name="f">The function to run.</param>
/// <returns>Whatever <paramref name="f" /> returned.</returns>
/// <remarks>
///     Rarely needed for a single operation, since each primitive opens a transaction of its own
///     where it needs one. It is for making some operations atomic together.
///
///     Build the graph in one of these, and the graph then keeps the first firing. That is most
///     important with <c>Cell.values</c>, which always fires immediately. It is also necessary for
///     <c>Stream.loop</c>, <c>Cell.loop</c> and <c>Behavior.loop</c>. The code must make and close
///     each of these in one transaction.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let run f = TransactionInternal.RunImpl(Func<_> f)

/// <summary>
///     Registers an action to run when a transaction starts.
/// </summary>
/// <param name="a">The action to run at the start of each transaction.</param>
/// <remarks>
///     The action can start its own transactions, and the hooks do not then run again. This exists
///     for implementing a timer system - it is how <c>SodaFlow.Time</c> delivers alarms - and is
///     rarely what calling code needs.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let onStart a =
    TransactionInternal.OnStartImpl(Action a)

/// <summary>
///     Runs an action after the current transaction closes, or immediately when no transaction is open.
/// </summary>
/// <param name="a">The action to run.</param>
/// <remarks>
///     The action runs with the transaction lock held, while the transaction closes. Thus, the rule
///     for a listener callback also applies: return quickly.
///     <para>
///         A transaction that fails while it propagates does not run the actions that this function
///         holds. A listener that throws is one cause of such a failure. Thus, an action here is not
///         a promise that this library keeps in each condition. Do not make one of these actions the
///         only code that gives a value to code that waits. A TaskCompletionSource, or a callback of
///         a different library, is such code. That code waits forever where the transaction fails.
///         mapAsync gives the supported path for an await on one value. The Execute methods of the
///         status that it answers with hold this rule.
///     </para>
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let post a = TransactionInternal.PostImpl(Action a)
