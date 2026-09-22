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
    ///     You can build this on any thread. The thread that builds the instance samples the
    ///     initial value, and the scheduler moves each subsequent change to the binding thread.
    ///     Thus, after the build only the binding thread writes the cached value.
    ///     Only the code that publishes the instance puts the building thread and the binding
    ///     thread in sequence. That code must do this in all conditions, because <c>comparer</c>
    ///     and <c>listener</c> are usual fields that a reader needs.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class OneWayBindableValue<T> : BindableValueBase, IOneWayBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     This field is necessary. The subscription is weak, thus this field keeps it alive.
        ///     It also keeps the graph above it alive. Do not make it a local variable.
        /// </summary>
        private readonly IListener listener;

        /// <summary>
        ///     The last value that the binding engine saw. After construction, only scheduled
        ///     work writes it, which means only the binding thread.
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

            // This code samples and listens in one transaction, thus no update enters the
            // interval between the two. It keeps the sample here and not after the transaction
            // for the same cause. After it attaches the listener, an update can arrive on a
            // different thread, and a write of the initial value after that removes the
            // update.
            //
            // The attachment of the listener puts this object into the graph before the
            // constructor returns. Thus, the listener can fire while the constructor runs. This
            // occurs when SodaFlow builds this object in a transaction that then updates the
            // same cell. The structure makes this safe, and not the sequence of events.
            // OnSourceChanged does not touch the cached value. It only posts to the scheduler.
            // Thus, the listener cannot write over the sample that this code takes. The scheduled
            // work runs after that, on the binding thread, and a newer update wins.
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
        ///     Applies an update. This method always posts and never raises on the calling
        ///     thread, because the callback runs in a transaction. A binding engine that responds
        ///     immediately can come back into the graph from a callback.
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
