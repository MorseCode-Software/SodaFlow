using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     Projects a <see cref="Cell{T}" /> onto a settable, observable property. Writes from the
    ///     view are pushed into the graph; the cell remains authoritative.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The setter is optimistic: it updates the cached value immediately (so the binding engine
    ///         reads back exactly what it wrote and does not fight the user's caret), then pushes the
    ///         value into the graph. Once the graph settles, a reconciliation pass samples the cell and
    ///         corrects the cached value if the graph rejected or normalized the write — for example an
    ///         input mask that upper-cases text, or a validation rule that discards it.
    ///     </para>
    ///     <para>
    ///         Safe to construct on any thread. The initial value is sampled by whichever thread
    ///         builds the instance and read by the binding thread; <see cref="ValueBox{T}" /> is what
    ///         orders the two. Every later change is marshaled through the scheduler.
    ///     </para>
    /// </remarks>
    private sealed class TwoWayBindableValue<T> : BindableValueBase, ITwoWayBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     Load-bearing. The subscription is weak, so this field is what keeps it alive. Do not
        ///     let it be refactored into a local.
        /// </summary>
        private readonly IListener listener;

        private readonly Action<T> write;

        /// <summary>
        ///     Boxed so the field can be volatile whatever <typeparamref name="T" /> is. See
        ///     <see cref="ValueBox{T}" />.
        /// </summary>
        private volatile ValueBox<T> box;

        /// <param name="cell">The authoritative value shown to the view.</param>
        /// <param name="write">
        ///     Receives values written by the view. Typically <c>sink.Send</c>. Invoked inside a
        ///     transaction opened by <c>Transaction.Post</c>, never from within a callback.
        /// </param>
        /// <param name="scheduler">
        ///     Marshals notifications onto the binding thread. Null resolves one ambiently.
        /// </param>
        /// <param name="comparer">
        ///     Decides whether a value has actually changed. Null uses the default comparer.
        /// </param>
        internal TwoWayBindableValue(
            Cell<T> cell,
            Action<T> write,
            IBindingScheduler? scheduler,
            IEqualityComparer<T>? comparer)
            : base(scheduler)
        {
            this.Cell = cell ?? throw new ArgumentNullException(nameof(cell));
            this.write = write ?? throw new ArgumentNullException(nameof(write));
            this.comparer = comparer ?? EqualityComparer<T>.Default;
            this.box = new ValueBox<T>(default!);

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
        public T Value
        {
            get => this.box.Value;
            set
            {
                this.ThrowIfDisposed();

                if (this.comparer.Equals(x: this.box.Value, y: value))
                {
                    return;
                }

                this.box = new ValueBox<T>(value);

                PostWrite(() =>
                {
                    // Checked again here, not only by the ThrowIfDisposed above. PostWrite
                    // defers whenever a transaction is already open, so a Dispose in between
                    // would otherwise still let this write reach the graph.
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    try
                    {
                        this.write(value);
                    }
                    finally
                    {
                        // Queued from inside the write rather than alongside it. If the write
                        // was itself deferred, queuing the reconciliation here keeps it behind
                        // the update the write produces — otherwise it could sample a stale
                        // cell and revert the user's edit before the send had happened.
                        //
                        // In a finally, because the cached value was already updated
                        // optimistically above. A write that throws would otherwise leave that
                        // value standing with nothing to correct it, and the equality check in
                        // the setter would then refuse to retry it — wedging the property for
                        // good. Reconciling regardless puts the cell's value back on screen.
                        this.ScheduleReconciliation();
                    }
                });
            }
        }

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

        /// <summary>
        ///     Queued from inside the write, so any update the write produced was posted first —
        ///     Sodium delivers it synchronously during the send. By the time this runs the cached
        ///     value is already correct in the common case, and this is a cheap no-op.
        /// </summary>
        private void ScheduleReconciliation() =>
            this.Scheduler.Post(() =>
            {
                if (this.IsDisposed)
                {
                    return;
                }

                T authoritative = this.Cell.SampleImpl();

                if (this.comparer.Equals(x: this.box.Value, y: authoritative))
                {
                    return;
                }

                this.box = new ValueBox<T>(authoritative);
                this.RaiseValueChanged();
            });

        protected override void DisposeCore() => this.listener.Unlisten();
    }
}