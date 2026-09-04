using System;
using System.Threading;
using JetBrains.Annotations;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Marshals notifications onto the thread the binding engine requires.
/// </summary>
[PublicAPI]
public interface IBindingScheduler
{
    /// <summary>
    ///     Whether the calling thread is the one this scheduler posts to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Best-effort, and deliberately biased. An implementation which cannot tell MUST
    ///         return <see langword="true" />. A wrong <see langword="true" /> costs nothing - it
    ///         gives up a diagnostic that was never guaranteed - while a wrong
    ///         <see langword="false" /> throws on correct code. Never answer
    ///         <see langword="false" /> unless the thread is known to be the wrong one.
    ///     </para>
    ///     <para>
    ///         A scheduler with no thread affinity of its own, one which runs work wherever it is
    ///         called, answers <see langword="true" /> unconditionally.
    ///     </para>
    /// </remarks>
    bool IsOnBindingThread { get; }

    /// <summary>
    ///     Queues <paramref name="action" /> for execution on the binding thread. Implementations
    ///     MUST preserve FIFO ordering and MUST NOT execute the action synchronously while a Sodium
    ///     transaction is in flight.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <remarks>
    ///     <para>
    ///         MUST NOT wait for the action to finish, either. This is called from inside a
    ///         transaction, and a transaction holds a process-wide lock for its whole duration.
    ///         An implementation that hands the action to the binding thread and blocks until it
    ///         returns deadlocks: the binding thread reaches this library through setters which
    ///         open transactions of their own, so it may already be waiting for the very lock the
    ///         caller of this method is holding. Queue and return — do not send and wait.
    ///     </para>
    ///     <para>
    ///         An implementation over a message loop gets this for nothing, since a dispatcher's
    ///         post is asynchronous by nature. It is a handwritten scheduler, or one built on a
    ///         send-and-wait primitive, that has to take care.
    ///     </para>
    /// </remarks>
    void Post(Action action);
}

/// <summary>
///     Posts through a captured <see cref="SynchronizationContext" />. Works unmodified for WPF
///     (<c>DispatcherSynchronizationContext</c>) and Avalonia (<c>AvaloniaSynchronizationContext</c>).
///     The <see cref="SynchronizationContext" /> used here must ensure that items are run exclusively,
///     not in parallel, and that their Post() method does not ever run the SendOrPostCallback delegate
///     directly, in which case it would become re-entrant.
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

    // Captured alongside the context because the two identify the binding thread in different
    // ways and neither is reliable alone. Taken from the constructing thread, which Capture and
    // the ambient resolution both call from the binding thread; a caller which passes a context
    // belonging to some other thread makes this the wrong id, and the check correspondingly
    // permissive - the harmless direction.
    private readonly int bindingThreadId;

    private readonly SynchronizationContext context;

    /// <summary>
    ///     Initializes a new instance posting through the given synchronization context.
    /// </summary>
    /// <param name="context">The context to post to. Usually the UI thread's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
    /// <remarks>
    ///     The <see cref="SynchronizationContext" /> used here must ensure that items are run exclusively,
    ///     not in parallel, and that their Post() method does not ever run the SendOrPostCallback delegate
    ///     directly, in which case it would become re-entrant.
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
    ///         Two ways to answer yes, and both have to fail before this says no. A dispatcher hands
    ///         out the same context instance on its own thread in the ordinary case, but not in
    ///         every one — a nested message pump or a priority-carrying copy can substitute another
    ///         — so the thread captured alongside it is the second answer. Erring toward yes is the
    ///         contract: see <see cref="IBindingScheduler.IsOnBindingThread" />.
    ///     </para>
    ///     <para>
    ///         The thread is compared first, and the order is load-bearing rather than incidental.
    ///         Reading <see cref="SynchronizationContext.Current" /> is not the cheap thread-local
    ///         fetch it looks like: on .NET Framework it goes through the execution context, and
    ///         this check measures 13.0ns with the context asked first against 3.1ns with the
    ///         thread id asked first — most of what a checked read of a bindable's value costs
    ///         there, 18.3ns against 6.4ns. The same reordering on .NET 8 is 2.3ns against 1.3ns,
    ///         so the case for it is made almost entirely by the older runtime, which the
    ///         libraries still support.
    ///     </para>
    ///     <para>
    ///         Asking the cheap question first means the answer is usually yes before the
    ///         expensive one is reached, and leaves the expensive one for the case it exists to
    ///         cover: a dispatcher which moved its work to another thread. Both numbers come from
    ///         BindableValueBenchmarks in SodaFlow.Benchmarks, which runs on both runtimes for
    ///         exactly this reason.
    ///     </para>
    /// </remarks>
    public bool IsOnBindingThread =>
        Environment.CurrentManagedThreadId == this.bindingThreadId
        || ReferenceEquals(objA: SynchronizationContext.Current, objB: this.context);

    /// <summary>
    ///     Posts through the captured context.
    /// </summary>
    /// <param name="action">The action to run on the binding thread.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action" /> is null.</exception>
    /// <remarks>
    ///     Always posted, never sent, even when the caller is already on the binding thread:
    ///     running inline would breach the contract on <see cref="IBindingScheduler.Post" /> for a
    ///     caller inside a transaction, and the handlers here are called from inside one. Posting
    ///     is also what keeps this off the wrong side of the deadlock described there, since
    ///     <see cref="SynchronizationContext.Post" /> returns without waiting.
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

    /// <summary>Captures the current thread's synchronization context.</summary>
    /// <exception cref="InvalidOperationException">No context is installed on this thread.</exception>
    public static SynchronizationContextBindingScheduler Capture()
    {
        SynchronizationContext? context = SynchronizationContext.Current;

        if (context == null)
        {
            throw new InvalidOperationException(
                "No SynchronizationContext is installed on the current thread. Capture the scheduler " +
                "from the UI thread, or supply an explicit IBindingScheduler.");
        }

        return new SynchronizationContextBindingScheduler(context);
    }
}

/// <summary>
///     Runs everything on the calling thread. Intended for unit tests, where notifications should
///     be observable synchronously and there is no UI thread to marshal to.
/// </summary>
/// <remarks>
///     Inline, but not unconditionally. Running inline while a transaction is in flight is exactly
///     what <see cref="IBindingScheduler.Post" /> forbids, and it is not a theoretical concern here:
///     the source-changed handlers are invoked from a listener callback, so an unconditionally
///     inline scheduler would raise <c>PropertyChanged</c> from inside the transaction and let a
///     handler re-enter the graph. A dispatcher-backed scheduler cannot do that; a test scheduler
///     that could, would exercise an ordering the real one never produces.
///     Deferring to the end of the current transaction costs a test nothing, because the queued
///     action still runs before the <c>Send</c> that produced it returns.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ImmediateBindingScheduler : IBindingScheduler
{
    /// <summary>The single instance. This type holds no state.</summary>
    public static readonly ImmediateBindingScheduler Instance = new();

    private ImmediateBindingScheduler()
    {
    }

    // ReSharper disable once InheritdocConsiderUsage
    /// <remarks>
    ///     Always true. This scheduler runs work on whichever thread hands it over, so every
    ///     thread is its binding thread and there is nothing to be wrong about.
    /// </remarks>
    public bool IsOnBindingThread => true;

    /// <summary>
    ///     Runs the action on the calling thread, once no transaction is in flight.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action" /> is null.</exception>
    /// <remarks>
    ///     Deferring to the close of the current transaction means running while that transaction
    ///     still holds the process-wide lock. Anything reached this way — a
    ///     <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged" /> subscriber,
    ///     most likely — therefore must not wait on another thread opening a transaction, because
    ///     that thread cannot open one until this one has closed. A dispatcher-backed scheduler
    ///     runs its actions after the transaction has released the lock and has no such
    ///     constraint, which is one more reason to keep this one to tests.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public void Post(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        // Immediately when no transaction is open - the common case in a test - and at the close
        // of the current one otherwise. Never from inside a callback, either way.
        TransactionInternal.PostImpl(action);
    }
}

/// <summary>Ambient scheduler resolution.</summary>
[PublicAPI]
public static class BindingScheduler
{
    /// <summary>
    ///     An explicit process-wide scheduler. Set this during startup when the binding thread has
    ///     no <see cref="SynchronizationContext" /> of its own to capture — a custom UI framework,
    ///     or a test host. When null, each bindable captures the
    ///     <see cref="SynchronizationContext" /> of the thread that constructed it.
    /// </summary>
    /// <remarks>
    ///     Bindable objects may be constructed on any thread, so a view model never needs to know which
    ///     thread the binding engine uses. What it does need is for one of these to be resolvable:
    ///     set this when the binding thread has no <see cref="SynchronizationContext" /> to capture,
    ///     or when construction happens somewhere there is no context to capture from.
    /// </remarks>
    public static IBindingScheduler? Default { get; set; }

    /// <summary>Convenience for tests and headless hosts.</summary>
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
