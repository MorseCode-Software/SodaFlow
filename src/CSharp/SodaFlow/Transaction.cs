using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow;

/// <summary>
///     A class for managing transactions.
/// </summary>
/// <remarks>
///     <para>
///         The process runs only one transaction at a time, on all of its threads. A thread that starts a
///         transaction waits until the transaction on a different thread ends. This is deliberate. It makes a
///         transaction atomic for each other thread, thus no code sees the graph in the middle of an update.
///         The sequence of the updates also stays deterministic, and the number of threads that push values
///         in makes no difference. Thus, code on more than one thread can use SodaFlow with no more
///         synchronization of its own.
///     </para>
///     <para>
///         The cost of that guarantee is that the transaction keeps the lock for all of its operation. That
///         includes each listener callback that it fires and each <see cref="Post(Action)" /> action that it queues,
///         which run while the transaction closes, with the lock held. While a callback runs, no other thread
///         can start a transaction. Thus, a callback must return quickly. Give work that takes a long time,
///         and work that blocks, to a different thread, and do not do it inline. A callback that waits on a
///         thread that tries to start a transaction causes a deadlock.
///     </para>
///     <para>
///         A nested transaction has no cost. A transaction that starts while one is open on the same thread
///         becomes part of the open transaction, and does not get the lock again. Thus, the primitives that
///         open their own transactions add no cost in <see cref="Run{T}" /> or <see cref="RunVoid" />.
///     </para>
/// </remarks>
[PublicAPI]
public static class Transaction
{
    /// <summary>
    ///     Gives true when there is a current transaction.
    /// </summary>
    /// <returns><code>true</code> if there is a current transaction, <code>false</code> otherwise.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsActive() => TransactionInternal.HasCurrentTransaction();

    /// <summary>
    ///     Runs the given action in one transaction.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <remarks>
    ///     Usually this is not necessary, because each primitive opens its own transaction automatically.
    ///     It is useful to run more than one reactive operation atomically.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RunVoid(Action action) =>
        TransactionInternal.RunImpl(() =>
        {
            action();
            return Unit.Value;
        });

    /// <summary>
    ///     Runs the given function in one transaction.
    /// </summary>
    /// <typeparam name="T">The type of the value returned.</typeparam>
    /// <param name="f">The function to run.</param>
    /// <returns>The return value of <paramref name="f" />.</returns>
    /// <remarks>
    ///     Usually this is not necessary, because each primitive opens its own transaction automatically.
    ///     It is useful to run more than one reactive operation atomically.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Run<T>(Func<T> f) => TransactionInternal.RunImpl(f);

    /// <summary>
    ///     Adds an action that runs at the start of each transaction.
    /// </summary>
    /// <param name="action">The action to run at the start of each transaction.</param>
    /// <remarks>
    ///     The action can start transactions itself, and the hooks do not then run again.
    ///     The primary use of this is for the implementation of a time/alarm system.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void OnStart(Action action) => TransactionInternal.OnStartImpl(action);

    /// <summary>
    ///     Runs an action after the current transaction closes
    ///     or immediately if there is no current transaction.
    /// </summary>
    /// <remarks>
    ///     A transaction that fails while it propagates does not run the actions that this method
    ///     holds. A listener that throws is one cause of such a failure. Thus, an action here is not
    ///     a promise that this library keeps in each condition. An action can give a value to code
    ///     that waits. A TaskCompletionSource, or a callback of a different library, is such code.
    ///     Use <see cref="Post(Action,Action{Exception})" /> for that action, and release the
    ///     waiting code there.
    /// </remarks>
    /// <param name="action">
    ///     The action to run after the current transaction closes
    ///     or immediately if there is no current transaction.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Post(Action action) => TransactionInternal.PostImpl(action);

    /// <summary>
    ///     Runs an action after the current transaction closes, or immediately if there is no
    ///     current transaction. Runs <paramref name="onFailure" /> where that action does not run or
    ///     does not complete.
    /// </summary>
    /// <remarks>
    ///     Use this where <paramref name="action" /> gives a value to code that waits, such as a
    ///     TaskCompletionSource or a callback of a different library. A transaction that fails while
    ///     it propagates does not run the actions that Post holds, and that code then waits forever.
    ///     <paramref name="onFailure" /> is where the caller releases it.
    ///     <para>
    ///         <paramref name="onFailure" /> runs one time, and only where
    ///         <paramref name="action" /> does not complete. Two conditions give that: the
    ///         transaction fails before it runs the action, and the action itself throws. The
    ///         argument is the exception of the transaction in the first condition, and the
    ///         exception of the action in the second. An action that completes runs no release.
    ///     </para>
    ///     <para>
    ///         A throw from <paramref name="onFailure" /> does not replace the exception that caused
    ///         it. The caller gets an AggregateException with that exception first, and the throw
    ///         from the release after it.
    ///     </para>
    /// </remarks>
    /// <param name="action">
    ///     The action to run after the current transaction closes
    ///     or immediately if there is no current transaction.
    /// </param>
    /// <param name="onFailure">
    ///     The action to run where <paramref name="action" /> does not complete, with the exception
    ///     that is the cause.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Post(Action action, Action<Exception> onFailure) =>
        TransactionInternal.PostImpl(action: action, onFailure: onFailure);
}
