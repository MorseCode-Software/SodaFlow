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
    ///     the instance and read by the binding thread; <see cref="ValueBox{T}" /> is what orders
    ///     the two. Every later change is marshaled through the scheduler.
    ///     Having no setter, this one carries no thread constraint at all past construction: the
    ///     cached value is written only by scheduled work, which is to say only on the binding
    ///     thread, and reading it is safe from anywhere. The writable values are narrower — see
    ///     <see cref="IWritableBindableValue{T}" />.
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
        ///     Boxed so the field can be volatile whatever <typeparamref name="T" /> is. See
        ///     <see cref="ValueBox{T}" />.
        /// </summary>
        private volatile ValueBox<T> box;

        internal OneWayBindableValue(
            Cell<T> cell,
            IBindingScheduler? scheduler,
            IEqualityComparer<T>? comparer)
            : base(scheduler)
        {
            this.Cell = cell ?? throw new ArgumentNullException(nameof(cell));
            this.comparer = comparer ?? EqualityComparer<T>.Default;

            // ReSharper disable once NullableWarningSuppressionIsUsed - This value will be replaced with a non-null
            // value in the transaction below when the cell is sampled, which happens before the constructor completes
            // and before the listener is attached, so nothing has a chance of modifying this.box before then.
            this.box = new ValueBox<T>(default!);

            // Sample and subscribe inside one transaction so no update can slip through the gap.
            // The sample is stored here rather than after the transaction for the same reason:
            // once the listener is attached an update can arrive on another thread, and writing
            // the initial value afterward would overwrite it.
            this.listener =
                TransactionInternal.RunImpl(() =>
                {
                    this.box = new ValueBox<T>(cell.SampleImpl());
                    return ListenToUpdates(cell: cell, handler: this.OnSourceChanged);
                });
        }

        /// <inheritdoc />
        public Cell<T> Cell { get; }

        /// <inheritdoc />
        public T Value => this.box.Value;

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

                if (this.comparer.Equals(x: this.box.Value, y: newValue))
                {
                    return;
                }

                this.box = new ValueBox<T>(newValue);
                this.RaiseValueChanged();
            });

        protected override void DisposeCore() => this.listener.Unlisten();
    }
}
