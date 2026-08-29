using System;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel
{
    /// <summary>
    ///     Marshals notifications onto the thread the binding engine requires.
    /// </summary>
    public interface IBindingScheduler
    {
        /// <summary>True when the calling thread is the binding thread.</summary>
        bool IsOnBindingThread { get; }

        /// <summary>
        ///     Queues <paramref name="action" /> for execution on the binding thread. Implementations
        ///     MUST preserve FIFO ordering and MUST NOT execute the action synchronously while a Sodium
        ///     transaction is in flight.
        /// </summary>
        void Post(Action action);
    }

    /// <summary>
    ///     Posts through a captured <see cref="SynchronizationContext" />. Works unmodified for WPF
    ///     (<c>DispatcherSynchronizationContext</c>) and Avalonia (<c>AvaloniaSynchronizationContext</c>).
    /// </summary>
    public sealed class SynchronizationContextBindingScheduler : IBindingScheduler
    {
        private static readonly SendOrPostCallback Callback = state => ((Action)state!)();

        private readonly SynchronizationContext context;

        public SynchronizationContextBindingScheduler(SynchronizationContext context) =>
            this.context = context ?? throw new ArgumentNullException(nameof(context));

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

        public bool IsOnBindingThread => ReferenceEquals(objA: SynchronizationContext.Current, objB: this.context);

        public void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            this.context.Post(d: Callback, state: action);
        }
    }

    /// <summary>
    ///     Runs everything inline. Intended for unit tests, where notifications should be observable
    ///     synchronously and there is no UI thread to marshal to.
    /// </summary>
    public sealed class ImmediateBindingScheduler : IBindingScheduler
    {
        public static readonly ImmediateBindingScheduler Instance = new();

        private ImmediateBindingScheduler()
        {
        }

        public bool IsOnBindingThread => true;

        public void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            action();
        }
    }

    /// <summary>Ambient scheduler resolution.</summary>
    public static class BindingScheduler
    {
        /// <summary>
        ///     An explicit process-wide scheduler. Set this during startup if your bindable values are not
        ///     constructed on the UI thread. When null, each bindable captures the
        ///     <see cref="SynchronizationContext" /> of the thread that constructed it.
        /// </summary>
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
}