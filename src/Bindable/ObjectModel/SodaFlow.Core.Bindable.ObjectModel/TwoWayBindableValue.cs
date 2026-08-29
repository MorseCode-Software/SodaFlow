using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel
{
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
        ///     <para>Instances must be constructed on the binding thread.</para>
        /// </remarks>
        private sealed class TwoWayBindableValue<T> : BindableValueBase, ITwoWayBindableValue<T>
        {
            private readonly Action<T> write;

            /// <summary>
            ///     Load-bearing. The subscription is weak, so this field is what keeps it alive. Do not
            ///     let it be refactored into a local.
            /// </summary>
            private readonly IListener listener;

            private readonly IEqualityComparer<T> comparer;

            private T value;

            /// <param name="cell">The authoritative value shown to the view.</param>
            /// <param name="write">
            ///     Receives values written by the view. Typically <c>sink.Send</c>. Invoked inside a
            ///     transaction opened by <c>Transaction.Post</c>, never from within a callback.
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
                this.value = default!;

                this.listener =
                    TransactionInternal.RunImpl(() =>
                    {
                        this.value = cell.SampleImpl();
                        return ListenToUpdates(cell: cell, handler: this.OnSourceChanged);
                    });
            }

            /// <inheritdoc />
            public Cell<T> Cell { get; }

            /// <inheritdoc />
            public T Value
            {
                get => this.value;
                set
                {
                    this.ThrowIfDisposed();

                    if (this.comparer.Equals(x: this.value, y: value))
                    {
                        return;
                    }

                    this.value = value;

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

                    if (this.comparer.Equals(x: this.value, y: newValue))
                    {
                        return;
                    }

                    this.value = newValue;
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

                    if (this.comparer.Equals(x: this.value, y: authoritative))
                    {
                        return;
                    }

                    this.value = authoritative;
                    this.RaiseValueChanged();
                });

            protected override void DisposeCore() => this.listener.Unlisten();
        }
    }
}