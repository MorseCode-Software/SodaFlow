using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     The entry point for values that originate in the view: control state such as selection,
    ///     scroll offset, or focus, pushed into the graph by a <c>OneWayToSource</c> binding.
    /// </summary>
    /// <remarks>
    ///     Safe to construct on any thread. The initial value is stored by whichever thread builds
    ///     the instance; nothing orders that against the binding thread beyond whatever publishes
    ///     the instance to it, which has to order them anyway for <c>comparer</c> and
    ///     <c>write</c>.
    ///     There is no scheduler here, because nothing flows back out to the view and so there is
    ///     nothing to marshal. The cached value is read and written by the binding engine, on the
    ///     binding thread, and by nothing else — see <see cref="IWritableBindableValue{T}" />.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class OneWayToSourceBindableValue<T> : IOneWayToSourceBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     Held only to answer which thread the binding engine is on. Nothing is posted through
        ///     it - nothing flows back out to the view - so it schedules no work here.
        /// </summary>
        private readonly IBindingScheduler scheduler;

        private readonly Action<T> write;

        /// <summary>
        ///     The value the binding engine last saw. Read and written on the binding thread only,
        ///     which is what lets it be an ordinary field: see <see cref="IWritableBindableValue{T}" />
        ///     for why nothing else touches it.
        /// </summary>
        private T cachedValue;

        private int disposed;

        /// <param name="write">Receives values written by the view. Typically <c>sink.Send</c>.</param>
        /// <param name="initialValue">
        ///     The value the graph sees before the view has written anything. The binding engine
        ///     typically writes the real value during the first layout pass.
        /// </param>
        /// <param name="comparer">
        ///     Decides whether a value has actually changed. Null uses the default comparer.
        /// </param>
        /// <param name="scheduler">
        ///     Identifies the binding thread, so that touching <see cref="Value" /> from elsewhere
        ///     is caught rather than left to corrupt the cached value quietly. Null resolves one
        ///     ambiently, as everywhere else.
        /// </param>
        internal OneWayToSourceBindableValue(
            Action<T> write,
            T initialValue,
            IEqualityComparer<T>? comparer,
            IBindingScheduler? scheduler)
        {
            this.comparer = comparer ?? EqualityComparer<T>.Default;
            this.scheduler = BindingScheduler.Resolve(scheduler);
            this.cachedValue = initialValue;
            this.write = write ?? throw new ArgumentNullException(nameof(write));
        }

        /// <inheritdoc />
        public T Value
        {
            get
            {
                this.scheduler.VerifyAccess("IOneWayToSourceBindableValue<T>.Value");

                return this.cachedValue;
            }

            set
            {
                this.scheduler.VerifyAccess("IOneWayToSourceBindableValue<T>.Value");

                if (Volatile.Read(ref this.disposed) != 0)
                {
                    return;
                }

                if (this.comparer.Equals(x: this.cachedValue, y: value))
                {
                    return;
                }

                this.cachedValue = value;

                // Checked again inside the post, not only above. PostWrite defers whenever a
                // transaction is already open, so a Dispose between the two would otherwise
                // still let this write reach the graph.
                PostWrite(() =>
                {
                    if (Volatile.Read(ref this.disposed) != 0)
                    {
                        return;
                    }

                    this.write(value);
                });
            }
        }

        /// <summary>
        ///     Stops accepting writes. The sink itself is left intact so downstream subscribers keep
        ///     observing the last value rather than faulting.
        /// </summary>
        // ReSharper disable once InheritdocConsiderUsage
        public void Dispose() => Interlocked.Exchange(location1: ref this.disposed, value: 1);
    }
}
