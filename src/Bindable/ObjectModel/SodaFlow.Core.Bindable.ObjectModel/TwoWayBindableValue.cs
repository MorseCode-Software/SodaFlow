using System;
using System.Collections.Generic;
using System.Threading;

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
    ///         corrects the cached value if the graph rejected or normalized the write operation — for example an
    ///         input mask that upper-cases text, or a validation rule that discards it.
    ///     </para>
    ///     <para>
    ///         Safe to construct on any thread. The initial value is sampled by whichever thread
    ///         builds the instance, and every later change is marshaled through the scheduler.
    ///         Nothing orders the constructing thread against the binding thread beyond whatever
    ///         publishes the instance to it — and that has to order them anyway, since
    ///         <c>comparer</c>, <c>listener</c> and <c>write</c> are ordinary fields a reader needs
    ///         just as much.
    ///     </para>
    ///     <para>
    ///         The setter writes the cached value on the calling thread rather than through the
    ///         scheduler, which it has to: the point of the optimistic update is that the binding
    ///         engine reads back what it just wrote, without a round trip in between. That is
    ///         sound because <see cref="Value" /> belongs to the binding engine and is read and
    ///         written there and nowhere else — see <see cref="IWritableBindableValue{T}" />.
    ///     </para>
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
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
        ///     The value the binding engine last saw. Read and written on the binding thread only,
        ///     which is what lets it be an ordinary field: see <see cref="IWritableBindableValue{T}" />
        ///     for why nothing else touches it.
        /// </summary>
        private T cachedValue;

        /// <summary>
        ///     How many refreshes have been queued and not yet run. Non-zero means the cached value
        ///     is not known to agree with the cell, which is what the setter's equality check needs
        ///     to know before it can treat a write as redundant.
        /// </summary>
        private int pendingRefreshes;

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

            // ReSharper disable once NullableWarningSuppressionIsUsed - This value will be replaced with a non-null
            // value in the transaction below when the cell is sampled, which happens before the constructor completes
            // and before the listener is attached, so nothing has a chance of modifying it before then.
            this.cachedValue = default!;

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
            get => this.cachedValue;
            set
            {
                this.ThrowIfDisposed();

                // Skipping the write because the cached value already matches is only sound while
                // that cached value is known to be the cell's. It is a record of what the cell held
                // when it was last sampled, so between an update and the refresh it queues the two
                // disagree - and a write matching the stale one would be dropped although the graph
                // never had it. While anything is queued, send and let the refresh behind it decide.
                if (Volatile.Read(ref this.pendingRefreshes) == 0
                    && this.comparer.Equals(x: this.cachedValue, y: value))
                {
                    return;
                }

                this.cachedValue = value;

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
                        // Run in a finally block, because the cached value was already updated
                        // optimistically above. A write operation that throws would otherwise leave that
                        // value standing with nothing to correct it, and the equality check in
                        // the setter would then refuse to retry it — wedging the property for
                        // good. Refreshing regardless puts the cell's value back on screen.
                        this.ScheduleRefreshFromCell();
                    }
                });
            }
        }

        /// <summary>
        ///     The update's own value is deliberately ignored in favor of sampling the cell. An
        ///     update carries what the cell held when it fired, and this runs later, on the binding
        ///     thread: by then the setter may have moved the cached value on, and writing a captured
        ///     value back would put an older one on screen in place of a newer one. Sampling asks
        ///     what is true now, which is the only question worth asking this late.
        /// </summary>
        /// <param name="newValue">Ignored. See the summary.</param>
        // ReSharper disable once UnusedParameter.Local - Required by the handler signature.
        private void OnSourceChanged(T newValue) => this.ScheduleRefreshFromCell();

        /// <summary>
        ///     Brings the cached value back in line with the cell.
        /// </summary>
        /// <remarks>
        ///     Every path that can leave the cache disagreeing with the cell ends here, and because
        ///     it samples rather than carrying a value, the order these run in does not matter: any
        ///     one of them arriving last leaves the same answer. That is what makes the queue safe
        ///     without reasoning about how a write interleaves with the update it produces.
        /// </remarks>
        private void ScheduleRefreshFromCell()
        {
            // Counted before the post, not inside it, so that a setter running between the two
            // still sees the refresh as outstanding.
            Interlocked.Increment(ref this.pendingRefreshes);

            this.Scheduler.Post(() =>
            {
                try
                {
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    T authoritative = this.Cell.SampleImpl();

                    if (this.comparer.Equals(x: this.cachedValue, y: authoritative))
                    {
                        return;
                    }

                    this.cachedValue = authoritative;
                    this.RaiseValueChanged();
                }
                finally
                {
                    // In a finally so that a disposal, or a throw out of the comparer, cannot
                    // leave the count standing - which would disable the equality check for the
                    // rest of this object's life.
                    Interlocked.Decrement(ref this.pendingRefreshes);
                }
            });
        }

        protected override void DisposeCore() => this.listener.Unlisten();
    }
}
