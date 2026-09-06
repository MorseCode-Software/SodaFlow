using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     Projects a <see cref="Cell{T}" /> onto
    ///     <see cref="System.ComponentModel.INotifyPropertyChanged" />.
    /// </summary>
    /// <remarks>
    ///     Safe to construct on any thread. The initial value is sampled by whichever thread builds
    ///     the instance, and every later change is marshaled through the scheduler, so after
    ///     construction the cached value is written only on the binding thread.
    ///     Nothing orders the constructing thread against the binding thread beyond whatever
    ///     publishes the instance to it — and that has to order them anyway, since
    ///     <c>comparer</c> and <c>listener</c> are ordinary fields a reader needs just as much.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class OneWayBindableValue<T> : BindableValueBase, IOneWayBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     Load-bearing. The subscription is weak, so this field is what keeps it alive — and it
        ///     transitively roots the upstream graph. Do not let it be refactored into a local.
        /// </summary>
        private readonly IListener listener;

        /// <summary>
        ///     The value the binding engine last saw. Written only by scheduled work after
        ///     construction, which is to say only on the binding thread.
        /// </summary>
        private T cachedValue;

        internal OneWayBindableValue(
            Cell<T> cell,
            IBindingScheduler? scheduler,
            IEqualityComparer<T>? comparer)
            : base(scheduler)
        {
            this.Cell = cell ?? throw new ArgumentNullException(nameof(cell));
            this.comparer = comparer ?? EqualityComparer<T>.Default;

            // ReSharper disable once NullableWarningSuppressionIsUsed - Replaced with the sampled value
            // in the transaction below, which happens before the constructor completes and before the
            // listener is attached.
            this.cachedValue = default!;

            // Sample and subscribe inside one transaction so no update can slip through the gap.
            // The sample is stored here rather than after the transaction for the same reason:
            // once the listener is attached an update can arrive on another thread, and writing
            // the initial value afterward would overwrite it.
            //
            // Attaching the listener publishes this object into the graph before the constructor
            // has returned, so the listener can fire while the constructor is still running - which
            // it does when this is constructed inside a transaction that goes on to update the same
            // cell. That is safe for a structural reason rather than a timing one: OnSourceChanged
            // does not touch the cached value at all, it only posts to the scheduler. Nothing the
            // listener can do writes over the sample being taken here; the scheduled work runs
            // afterward, on the binding thread, and a newer update correctly wins.
            this.listener =
                TransactionInternal.RunImpl(() =>
                {
                    this.cachedValue = cell.SampleImpl();
                    return ListenToUpdates(cell: cell, handler: this.OnSourceChanged);
                });
        }

        /// <inheritdoc />
        public Cell<T> Cell { get; }

        /// <inheritdoc />
        public T Value
        {
            get
            {
                this.Scheduler.VerifyAccess("IOneWayBindableValue<T>.Value");

                return this.cachedValue;
            }
        }

        /// <summary>
        ///     Applies an incoming update. Always posted rather than raised inline: the callback runs
        ///     inside a Sodium transaction, and a binding engine reacting synchronously could re-enter
        ///     the graph from within a callback.
        /// </summary>
        private void OnSourceChanged(T newValue) =>
            this.Scheduler.Post(() =>
            {
                if (this.IsDisposed)
                {
                    return;
                }

                if (this.comparer.Equals(x: this.cachedValue, y: newValue))
                {
                    return;
                }

                this.cachedValue = newValue;
                this.RaiseValueChanged();
            });

        protected override void DisposeCore() => this.listener.Unlisten();
    }
}
