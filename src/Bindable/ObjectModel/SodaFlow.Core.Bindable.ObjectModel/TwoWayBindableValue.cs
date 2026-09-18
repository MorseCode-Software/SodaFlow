using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     Shows a <see cref="Cell{T}" /> as a property that the view can set and observe. The
    ///     view sends its writes into the graph. The cell stays the authority on the value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The setter is optimistic. It writes the cached value immediately, thus the binding
    ///         engine reads back the value that it wrote and does not move the caret of the user.
    ///         Then it sends the value into the graph. After the graph becomes stable, a second
    ///         pass samples the cell and corrects the cached value. The graph can refuse the write
    ///         or change it. An input mask that makes text uppercase does this, and so does a rule
    ///         that discards a value.
    ///     </para>
    ///     <para>
    ///         That pass also announces the stable value, and it announces the value when the
    ///         graph makes no change. The graph can accept a write with no change, but each
    ///         binding other than the one that wrote shows the previous value. Thus the
    ///         write is a change to all of them. The notification carries the value that the cell
    ///         settled on and not the optimistic value. Thus it announces no value that the graph
    ///         refused.
    ///     </para>
    ///     <para>
    ///         You can build this on any thread. The thread that builds the instance samples
    ///         the initial value, and the scheduler moves each subsequent change to the binding
    ///         thread. Only the code that publishes the instance puts the building thread and
    ///         the binding thread in sequence. That code must do this in all conditions, because
    ///         <c>comparer</c>, <c>listener</c> and <c>write</c> are usual fields that a reader
    ///         needs.
    ///     </para>
    ///     <para>
    ///         The setter writes the cached value on the calling thread and does not use the
    ///         scheduler. It must do this, because the optimistic write lets the binding engine
    ///         read back the value that it wrote, and does not wait. This is correct, because
    ///         <see cref="Value" /> belongs to the binding engine, which reads and writes it in
    ///         one place only. See <see cref="IWritableBindableValue{T}" />.
    ///     </para>
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class TwoWayBindableValue<T> : BindableValueBase, ITwoWayBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     This field is necessary. The subscription is weak, thus this field keeps it
        ///     alive. Do not make it a local variable.
        /// </summary>
        private readonly IListener listener;

        private readonly Action<T> write;

        /// <summary>
        ///     The last value that the binding engine saw. Only the binding thread reads and
        ///     writes it, which is what lets it be a usual field. See
        ///     <see cref="IWritableBindableValue{T}" /> for the cause.
        /// </summary>
        private T cachedValue;

        /// <summary>
        ///     The last value that <see cref="BindableValueBase.PropertyChanged" /> announced.
        ///     Each observer other than the one that wrote shows this value.
        /// </summary>
        /// <remarks>
        ///     This is different from <see cref="cachedValue" />, and the difference is
        ///     necessary. The setter writes the cached value optimistically. Thus after the graph
        ///     accepts a write, the cache agrees with the cell, but each binding on this property
        ///     shows the previous value. When this code compares the cell against the cache alone cannot
        ///     find that condition, and reads it as no change. Only the binding thread touches
        ///     this field, for the cause that applies to the cached value.
        /// </remarks>
        private T lastNotifiedValue;

        /// <summary>
        ///     The number of refresh operations in the queue that did not run. A value more than
        ///     zero means that the cached value can disagree with the cell. The equality test in
        ///     the setter needs this before it can discard a write.
        /// </summary>
        private int pendingRefreshes;

        /// <param name="cell">The authoritative value shown to the view.</param>
        /// <param name="write">
        ///     Gets the values that the view writes. This is usually <c>sink.Send</c>. SodaFlow
        ///     calls it in a transaction that <c>Transaction.Post</c> opens, and not in a
        ///     callback.
        /// </param>
        /// <param name="scheduler">
        ///     Moves notifications to the binding thread. A null value selects the ambient
        ///     scheduler.
        /// </param>
        /// <param name="comparer">
        ///     Tells you if a value changed. A null value selects the default comparer.
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

            // ReSharper disable once NullableWarningSuppressionIsUsed - Replaced with the sampled value
            // in the transaction below, which happens before the constructor completes and before the
            // listener is attached.
            this.cachedValue = default!;

            // ReSharper disable once NullableWarningSuppressionIsUsed - As above.
            this.lastNotifiedValue = default!;

            // The attachment of the listener puts this object into the graph before the
            // constructor returns. Thus the listener can fire while the constructor runs. This
            // occurs when SodaFlow builds this object in a transaction that then updates the
            // same cell. The structure makes this safe, and not the sequence of events.
            // OnSourceChanged does not touch the cached value. It only posts to the scheduler.
            // Thus the listener cannot write over the sample that this code takes. The scheduled
            // work runs after that, on the binding thread, and a newer update wins.

            this.listener =
                TransactionInternal.RunImpl(() =>
                {
                    this.cachedValue = cell.SampleImpl();

                    // This announces nothing, and it must announce nothing. A binding reads the
                    // property when it attaches, thus the binding sees the initial value.
                    this.lastNotifiedValue = this.cachedValue;

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
                this.Scheduler.VerifyAccess("ITwoWayBindableValue<T>.Value");

                return this.cachedValue;
            }

            set
            {
                this.Scheduler.VerifyAccess("ITwoWayBindableValue<T>.Value");
                this.ThrowIfDisposed();

                // This code can discard a write when the cached value is equal, but only while
                // that cached value is the value of the cell. The cached value is a record of the
                // cell at the last sample. Thus between an update and the refresh that it queues,
                // the two disagree, and this code can discard a write that is equal to the stale
                // value although the graph never got it. While the queue holds work, send the
                // value and let the refresh decide.
                if (Volatile.Read(ref this.pendingRefreshes) == 0
                    && this.comparer.Equals(x: this.cachedValue, y: value))
                {
                    return;
                }

                this.cachedValue = value;

                PostWrite(() =>
                {
                    // This code tests again here, and does not depend on the ThrowIfDisposed
                    // above. PostWrite defers while a transaction is open. Thus a Dispose
                    // between the two can let this write reach the graph.
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
                        // This runs in a finally block, because the code above wrote the cached
                        // value optimistically. A write that throws can leave that value with no
                        // correction, and the equality test in the setter then discards a second
                        // attempt. That stops the property permanently. A refresh in all
                        // conditions puts the value of the cell back on the screen.
                        this.ScheduleRefreshFromCell();
                    }
                });
            }
        }

        /// <summary>
        ///     This method discards the value of the update and samples the cell. An update
        ///     carries the value that the cell held at the firing, and this method runs after
        ///     that, on the binding thread. The setter can write the cached value in that
        ///     interval, and a captured value can put a previous value on the screen and not
        ///     a newer one. A sample gets the value that is correct now.
        /// </summary>
        /// <param name="newValue">Ignored. See the summary.</param>
        // ReSharper disable once UnusedParameter.Local - Required by the handler signature.
        private void OnSourceChanged(T newValue) => this.ScheduleRefreshFromCell();

        /// <summary>
        ///     Brings the cached value back in line with the cell.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each path that can make the cache disagree with the cell ends here. This
        ///         method samples and carries no value, thus the sequence of these operations
        ///         does not change the result. Each one of them gives the same answer when it is
        ///         last. This makes the queue safe, and you do not have to know how a write and
        ///         the update that it makes come together.
        ///     </para>
        ///     <para>
        ///         The sample has a cost, and you must know that cost before you remove it.
        ///         Posted work runs after the transaction that sends closes. Thus there is no
        ///         transaction to join and this method opens one. That measured 45ns on .NET 8
        ///         and 62ns on .NET Framework, which is the cost of an empty transaction on those
        ///         platforms. The cost is the transaction and not the sample. One complete update
        ///         to a two-way value costs 492ns and 696ns, thus the sample is approximately one
        ///         tenth. A one-way value makes no sample and pays none of this cost.
        ///     </para>
        ///     <para>
        ///         This does make an update take the transaction lock of the process two times
        ///         and not one time: one time to send, and one more time to sample. That is a
        ///         delay when threads compete for the lock, and not a cost in throughput. Thus do
        ///         not change this unless a measurement tells you differently. See
        ///         BindableRefreshBenchmarks in SodaFlow.Benchmarks.
        ///     </para>
        ///     <para>
        ///         One change can give a benefit: do not queue a refresh while the queue holds
        ///         one. The refresh operations in the queue give the same result, because they
        ///         all sample the same stable cell. The first one does the work and the others
        ///         find no change. Thus one sample and not many changes no notification and
        ///         no value. You must know why this code does not do that. The change cannot use
        ///         pendingRefreshes, which means "the cache can disagree with the cell" and not
        ///         "a post is in the queue". The two are different when a refresh ran and a newer update
        ///         arrived. A simple test on that field either discards the update and leaves the
        ///         cache stale permanently, or clears the count before that point and lets the setter
        ///         discard a write. A correct change needs a generation number that the method
        ///         compares from start to end. That is a second concurrent rule on the field that
        ///         the setter already depends on, for no saving unless updates arrive in groups
        ///         in one turn of the dispatcher.
        ///     </para>
        /// </remarks>
        private void ScheduleRefreshFromCell()
        {
            // This counts before the post and not in it. Thus a setter that runs between the
            // two sees that the refresh is in the queue.
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

                    // Two values can be behind the cell, and each one is a cause to announce.
                    // Thus there is no work only when the two agree with the cell.
                    //
                    // The cached value is the value that the control wrote optimistically. It is
                    // behind when the graph refused that write or changed it, which is the
                    // condition that gives the correction back to the control.
                    //
                    // The last notified value is the value that each other observer got. It is
                    // behind when the graph accepted a write with no change. The cache agrees
                    // with the cell because the setter wrote it first, but no binding on this
                    // property got a notification. A property that one control can write and
                    // another cannot follow is not a bindable value, thus this condition must
                    // announce.
                    //
                    // The short-circuit is deliberate. A first test that fails shows that
                    // this is not the return above. Thus the second test adds no answer,
                    // and a comparer is code that this class does not control.
                    if (this.comparer.Equals(x: this.cachedValue, y: authoritative)
                        && this.comparer.Equals(x: this.lastNotifiedValue, y: authoritative))
                    {
                        return;
                    }

                    // This code writes the field only on the path that announces. On the early
                    // return the cached value is equal, and a write there replaces the value of
                    // the control with the value of the graph and tells no observer. A comparer
                    // that ignores part of a value shows that difference. A comparer
                    // that ignores letter case is such a comparer, and nothing corrects the
                    // difference.
                    this.cachedValue = authoritative;
                    this.lastNotifiedValue = authoritative;
                    this.RaiseValueChanged();
                }
                finally
                {
                    // This runs in a finally block. Thus a disposal, or a throw from the
                    // comparer, cannot leave the count too high. A count that is too high stops
                    // the equality test for the full life of this object.
                    Interlocked.Decrement(ref this.pendingRefreshes);
                }
            });
        }

        protected override void DisposeCore() => this.listener.Unlisten();
    }
}
