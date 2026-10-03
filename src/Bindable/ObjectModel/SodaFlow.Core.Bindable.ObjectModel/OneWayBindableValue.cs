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
    ///     You can build this on any thread. The building transaction samples the initial value
    ///     when it closes, and the scheduler moves each subsequent change to the binding
    ///     thread. Thus, only the binding thread reads the sample and writes the cached
    ///     value. Only the code that publishes the instance puts the building thread and the
    ///     binding thread in sequence. That code must do this in all conditions, because
    ///     <c>comparer</c> and <c>listener</c> are usual fields that a reader needs.
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
        ///     The last value that the binding engine saw. Only the binding thread reads and
        ///     writes it. Call <see cref="ResolveInitialValue" /> before each use.
        /// </summary>
        private T cachedValue;

        /// <summary>
        ///     The sample from the constructor, until the first use of the cached value. Then it
        ///     is null. See <see cref="SampleAtCloseAndListenToUpdates{T}" /> for the cause.
        /// </summary>
        private InitialSample<T>? initialValue;

        internal OneWayBindableValue(
            Cell<T> cell,
            IBindingScheduler scheduler,
            IEqualityComparer<T>? comparer)
            : base(scheduler)
        {
            this.Cell = cell ?? throw new ArgumentNullException(nameof(cell));
            this.comparer = comparer ?? EqualityComparer<T>.Default;

            // ReSharper disable once NullableWarningSuppressionIsUsed - Never read. ResolveInitialValue
            // replaces it with the sample before the first use of the cached value.
            this.cachedValue = default!;

            // The attachment of the listener puts this object into the graph before the
            // constructor returns. Thus, the listener can fire while the constructor runs. This
            // occurs when SodaFlow builds this object in a transaction that then updates the
            // same cell. The structure makes this safe, and not the sequence of events.
            // OnSourceChanged does not touch the cached value. It only posts to the scheduler.
            // Thus, the listener cannot write over the sample that this code takes. The scheduled
            // work runs after that, on the binding thread, and a newer update wins.
            (this.initialValue, this.listener) =
                SampleAtCloseAndListenToUpdates(cell: cell, handler: this.OnSourceChanged);
        }

        /// <inheritdoc />
        public Cell<T> Cell { get; }

        /// <inheritdoc />
        public T Value
        {
            get
            {
                this.Scheduler.VerifyAccess("IOneWayBindableValue<T>.Value");
                this.ResolveInitialValue();

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

                this.ResolveInitialValue();

                if (this.comparer.Equals(x: this.cachedValue, y: newValue))
                {
                    return;
                }

                this.cachedValue = newValue;
                this.RaiseValueChanged();
            });

        /// <summary>
        ///     Puts the sample from the constructor into the cached value, one time only.
        /// </summary>
        private void ResolveInitialValue()
        {
            if (this.initialValue == null)
            {
                return;
            }

            this.cachedValue = this.initialValue.Read();
            this.initialValue = null;
        }

        protected override void DisposeCore() => this.listener.Unlisten();
    }
}
