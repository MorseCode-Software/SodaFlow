using System;
using System.Threading;
using JetBrains.Annotations;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Moves notifications to the thread that the binding engine needs.
/// </summary>
[PublicAPI]
public interface IBindingScheduler
{
    /// <summary>
    ///     Whether the calling thread is the one this scheduler posts to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This answer is an estimate, and it is deliberately not symmetrical. An
    ///         implementation that cannot tell MUST return <see langword="true" />. An incorrect
    ///         <see langword="true" /> has no cost, because it only gives up a diagnostic that
    ///         was never a guarantee. An incorrect <see langword="false" /> throws on correct
    ///         code. Answer <see langword="false" /> only when you know that the thread is the
    ///         incorrect one.
    ///     </para>
    ///     <para>
    ///         A scheduler that has no thread of its own runs work on the thread that calls it.
    ///         Such a scheduler answers <see langword="true" /> in all conditions.
    ///     </para>
    /// </remarks>
    bool CheckAccess();

    /// <summary>
    ///     Puts <paramref name="action" /> in the queue for the binding thread. An
    ///     implementation MUST keep the first-in first-out sequence. It MUST NOT run the action
    ///     immediately while a transaction is open.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <remarks>
    ///     <para>
    ///         An implementation MUST NOT wait for the action to complete. SodaFlow calls this
    ///         method in a transaction, and a transaction holds a lock for the full process while
    ///         it runs. An implementation that gives the action to the binding thread and then
    ///         waits causes a deadlock. The binding thread comes into this library through
    ///         setters that open transactions of their own. Thus, it can already wait for the lock
    ///         that the caller of this method holds. Put the action in the queue and return. Do
    ///         not send it and wait.
    ///     </para>
    ///     <para>
    ///         An implementation on a message loop gets this behavior at no cost, because the
    ///         post of a dispatcher is asynchronous. A scheduler that you write yourself, or one
    ///         on a primitive that sends and waits, must be careful.
    ///     </para>
    /// </remarks>
    void Post(Action action);
}

/// <summary>
///     Posts through a captured <see cref="SynchronizationContext" />. This works with no change
///     for WPF, which uses <c>DispatcherSynchronizationContext</c>, and for Avalonia, which uses
///     <c>AvaloniaSynchronizationContext</c>. The <see cref="SynchronizationContext" /> must run
///     items one at a time and not in parallel. Its Post() method must not run the
///     SendOrPostCallback delegate directly, because that makes this scheduler re-entrant.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class SynchronizationContextBindingScheduler : IBindingScheduler
{
    private static readonly SendOrPostCallback Callback =
        static state =>
        {
            Action? a = state as Action;
            a?.Invoke();
        };

    // SodaFlow captures this with the context, because the two identify the binding thread in
    // different ways and one alone is not sufficient. It comes from the building thread.
    // Capture and the ambient resolution both run on the binding thread. A caller that supplies
    // a context of a different thread makes this identifier incorrect, and makes the test more
    // permissive. That is the direction with no risk.
    private readonly int bindingThreadId;

    private readonly SynchronizationContext context;

    /// <summary>
    ///     Creates an instance that posts through the given synchronization context.
    /// </summary>
    /// <param name="context">The context to post to. This is usually the UI thread.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
    /// <remarks>
    ///     The <see cref="SynchronizationContext" /> must run items one at a time and not in
    ///     parallel. Its Post() method must not run the SendOrPostCallback delegate directly,
    ///     because that makes this scheduler re-entrant.
    /// </remarks>
    public SynchronizationContextBindingScheduler(SynchronizationContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.bindingThreadId = Environment.CurrentManagedThreadId;
    }

    // ReSharper disable once InheritdocConsiderUsage
    /// <summary>
    ///     Whether the calling thread is the one this scheduler posts to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         There are two methods to answer yes, and the two tests must fail before this method
    ///         answers no. A dispatcher usually supplies the same context instance on its own
    ///         thread, but not in each condition. A nested message loop, or a copy that carries a
    ///         priority, can supply a different one. Thus, the thread that SodaFlow captures with
    ///         the context is the second answer. An answer of yes when the method is not sure is
    ///         the contract. See <see cref="IBindingScheduler.CheckAccess()" />.
    ///     </para>
    ///     <para>
    ///         This method compares the thread first, and that sequence is necessary. A read of
    ///         <see cref="SynchronizationContext.Current" /> is not a cheap thread-local read. On
    ///         .NET Framework it goes through the execution context. This test measures 13.0ns
    ///         when it reads the context first, and 3.1ns when it reads the thread identifier
    ///         first. That is most of the cost of a checked read of a bindable value on that
    ///         platform, which is 18.3ns against 6.4ns. The same change on .NET 8 is 2.3ns
    ///         against 1.3ns. Thus, the older runtime, which these libraries support, is the cause
    ///         of this sequence.
    ///     </para>
    ///     <para>
    ///         The cheap test first usually gives the answer yes before the method does the
    ///         expensive test. That leaves the expensive test for the condition it covers: a
    ///         dispatcher that moved its work to a different thread. BindableValueBenchmarks in
    ///         SodaFlow.Benchmarks supplies these numbers, and it runs on the two runtimes for
    ///         this cause.
    ///     </para>
    /// </remarks>
    public bool CheckAccess() =>
        Environment.CurrentManagedThreadId == this.bindingThreadId
        || ReferenceEquals(objA: SynchronizationContext.Current, objB: this.context);

    /// <summary>
    ///     Posts through the captured context.
    /// </summary>
    /// <param name="action">The action to run on the binding thread.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action" /> is null.</exception>
    /// <remarks>
    ///     This method always posts and never sends, also when the caller is on the binding
    ///     thread. A run on the calling thread breaks the contract on
    ///     <see cref="IBindingScheduler.Post" /> for a caller in a transaction, and SodaFlow
    ///     calls these handlers in one. A post also prevents the deadlock that the contract
    ///     describes, because <see cref="SynchronizationContext.Post" /> returns and does not
    ///     wait.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public void Post(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        this.context.Post(d: Callback, state: action);
    }

    /// <summary>Captures the synchronization context of the current thread.</summary>
    /// <exception cref="InvalidOperationException">This thread has no context.</exception>
    public static SynchronizationContextBindingScheduler Capture()
    {
        SynchronizationContext? context = SynchronizationContext.Current;

        if (context == null)
        {
            throw new InvalidOperationException(
                "No SynchronizationContext is installed on the current thread. Capture the scheduler "
                + "from the UI thread, or supply an explicit IBindingScheduler.");
        }

        return new SynchronizationContextBindingScheduler(context);
    }
}

/// <summary>
///     Runs all work on the calling thread. Use this in a unit test, where a notification must
///     be visible immediately and there is no UI thread.
/// </summary>
/// <remarks>
///     This scheduler runs work on the calling thread, but not in each condition. A run on the
///     calling thread while a transaction is open is what <see cref="IBindingScheduler.Post" />
///     prevents, and the risk exists. SodaFlow calls the source-changed handlers from a listener
///     callback. Thus, a scheduler that always runs on the calling thread raises
///     <c>PropertyChanged</c> in the transaction and lets a handler come back into the graph. A
///     scheduler on a dispatcher cannot do that. A test scheduler that can do it tests a sequence
///     that the true scheduler never makes. A wait for the end of the current transaction costs a
///     test nothing, because the action in the queue runs before the <c>Send</c> that made
///     it returns.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ImmediateBindingScheduler : IBindingScheduler
{
    /// <summary>The one instance. This type holds no state.</summary>
    public static readonly ImmediateBindingScheduler Instance = new();

    private ImmediateBindingScheduler()
    {
    }

    // ReSharper disable once InheritdocConsiderUsage
    /// <remarks>
    ///     This is always true. The scheduler runs work on the thread that supplies it. Thus,
    ///     each thread is its binding thread and no answer can be incorrect.
    /// </remarks>
    public bool CheckAccess() => true;

    /// <summary>
    ///     Runs the action on the calling thread, when no transaction is open.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action" /> is null.</exception>
    /// <remarks>
    ///     A wait for the close of the current transaction makes the action run while that
    ///     transaction holds the lock for the full process. Thus, code that this action reaches
    ///     must not wait for a different thread to open a transaction, because that thread cannot
    ///     open one until this transaction closes. A
    ///     <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged" /> subscriber
    ///     is the most probable example. A scheduler on a dispatcher runs its actions after the
    ///     transaction releases the lock and has no such limit. That is one more cause to use
    ///     this scheduler only in a test.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public void Post(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        // This runs immediately when no transaction is open, which is the usual condition in a
        // test. If a transaction is open, it runs at the close of that transaction. It never runs
        // in a callback.
        TransactionInternal.PostImpl(action);
    }
}

/// <summary>Finds the ambient scheduler.</summary>
[PublicAPI]
public static class BindingScheduler
{
    /// <summary>
    ///     A scheduler for the full process. Set this at startup when the binding thread has no
    ///     <see cref="SynchronizationContext" /> of its own. A UI framework that you write
    ///     yourself, or a test host, has no such context. When this is null, each bindable
    ///     captures the <see cref="SynchronizationContext" /> of the thread that constructed
    ///     it.
    /// </summary>
    /// <remarks>
    ///     You can build a bindable object on any thread, thus a view model does not have to
    ///     know which thread the binding engine uses. But one of these must be available. Set
    ///     this when the binding thread has no <see cref="SynchronizationContext" />, or when
    ///     a build occurs where there is no context to capture.
    /// </remarks>
    public static IBindingScheduler? Default { get; set; }

    /// <summary>A scheduler for a test and for a host with no UI.</summary>
    public static IBindingScheduler Immediate => ImmediateBindingScheduler.Instance;

    internal static IBindingScheduler Resolve(IBindingScheduler? scheduler)
    {
        if (scheduler != null)
        {
            return scheduler;
        }

        if (Default != null)
        {
            return Default;
        }

        SynchronizationContext? context = SynchronizationContext.Current;

        return context != null
            ? new SynchronizationContextBindingScheduler(context)
            : ImmediateBindingScheduler.Instance;
    }
}
