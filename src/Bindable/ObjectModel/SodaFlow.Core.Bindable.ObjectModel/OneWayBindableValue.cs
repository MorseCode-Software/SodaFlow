using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel
{
    public static partial class BindableCoreExtensionMethods
    {
        /// <summary>
        ///     Projects a <see cref="Cell{T}" /> onto
        ///     <see cref="System.ComponentModel.INotifyPropertyChanged" />.
        /// </summary>
        /// <remarks>
        ///     Instances must be constructed on the binding thread, as
        ///     <see cref="TwoWayBindableValue{T}" /> also requires. Every later write to
        ///     <see cref="Value" /> is marshalled through the scheduler, but the initial sample is
        ///     written directly by the constructor, and the field cannot be made volatile because
        ///     <typeparamref name="T" /> may be a value type. Constructing elsewhere and handing the
        ///     instance over without synchronizing the handover lets the binding thread read
        ///     <see langword="default" /> instead of the sampled value.
        /// </remarks>
        private sealed class OneWayBindableValue<T> : BindableValueBase, IOneWayBindableValue<T>
        {
            /// <summary>
            ///     Load-bearing. The subscription is weak, so this field is what keeps it alive — and it
            ///     transitively roots the upstream graph. Do not let it be refactored into a local.
            /// </summary>
            private readonly IListener listener;

            private readonly IEqualityComparer<T> comparer;

            internal OneWayBindableValue(
                Cell<T> cell,
                IBindingScheduler? scheduler,
                IEqualityComparer<T>? comparer)
                : base(scheduler)
            {
                this.Cell = cell ?? throw new ArgumentNullException(nameof(cell));
                this.comparer = comparer ?? EqualityComparer<T>.Default;
                this.Value = default!;

                // Sample and subscribe inside one transaction so no update can slip through the gap.
                this.listener =
                    TransactionInternal.RunImpl(() =>
                    {
                        this.Value = cell.SampleImpl();
                        return ListenToUpdates(cell: cell, handler: this.OnSourceChanged);
                    });
            }

            /// <inheritdoc />
            public Cell<T> Cell { get; }

            /// <inheritdoc />
            public T Value { get; private set; }

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

                    if (this.comparer.Equals(x: this.Value, y: newValue))
                    {
                        return;
                    }

                    this.Value = newValue;
                    this.RaiseValueChanged();
                });

            protected override void DisposeCore() => this.listener.Unlisten();
        }
    }
}