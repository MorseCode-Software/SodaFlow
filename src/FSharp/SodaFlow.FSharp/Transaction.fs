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
///         a promise that this library keeps in each condition. An action can give a value to code
///         that waits. A TaskCompletionSource, or a callback of a different library, is such code.
///         Use <c>postWithReleaseOnFailure</c> for that action, and release the waiting code there.
///     </para>
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let post a = TransactionInternal.PostImpl(Action a)

/// <summary>
///     Runs an action after the current transaction closes, or immediately when no transaction is
///     open. Runs <paramref name="onFailure" /> where that action does not run or does not
///     complete.
/// </summary>
/// <param name="onFailure">
///     The action to run where <paramref name="a" /> does not complete, with the exception that is
///     the cause.
/// </param>
/// <param name="a">The action to run.</param>
/// <remarks>
///     The action runs with the transaction lock held, while the transaction closes. Thus, the rule
///     for a listener callback also applies: return quickly.
///     <para>
///         Use this where <paramref name="a" /> gives a value to code that waits, such as a
///         TaskCompletionSource or a callback of a different library. A transaction that fails while
///         it propagates does not run the actions that <c>post</c> holds, and that code then waits
///         forever. <paramref name="onFailure" /> is where the caller releases it.
///     </para>
///     <para>
///         <paramref name="onFailure" /> runs one time, and only where <paramref name="a" /> does
///         not complete. Two conditions give that: the transaction fails before it runs the action,
///         and the action itself throws. The argument is the exception of the transaction in the
///         first condition, and the exception of the action in the second. An action that completes
///         runs no release.
///     </para>
///     <para>
///         A throw from <paramref name="onFailure" /> does not replace the exception that caused it.
///         The caller gets an AggregateException with that exception first, and the throw from the
///         release after it.
///     </para>
///     <para>
///         This function is what C# spells as a second overload of Post. F# has no optional
///         parameter on a let-bound function, thus the two forms are two names here.
///     </para>
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let postWithReleaseOnFailure (onFailure: exn -> unit) a =
    TransactionInternal.PostImpl(Action a, Action<exn> onFailure)
