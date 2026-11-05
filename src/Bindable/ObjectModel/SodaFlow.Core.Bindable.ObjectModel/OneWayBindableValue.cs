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
    ///     You can build this on any thread. The building transaction samples the
    ///     initial value when it closes, and the scheduler moves each subsequent
    ///     change to the binding thread. Thus, only the binding thread reads the
    ///     sample and writes the cached value. Only the code that publishes the
    ///     instance puts the building thread and the binding thread in sequence. That
    ///     code must do this in all conditions, because <c>comparer</c> and
    ///     <c>listener</c> are usual fields that a reader needs.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class OneWayBindableValue<T> : BindableValueBase, IOneWayBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     This field is necessary. The subscription is weak, thus this field keeps it
        ///     alive.
        ///     It also keeps the graph above it alive. Do not make it a local variable.
        /// </summary>
        private readonly IListener listener;

        /// <summary>
        ///     The updates that the listener posted and that no refresh removed, in
        ///     sequence, each with the outcome of its transaction. See
        ///     <see cref="OnSourceChanged" />. The lock is the queue itself.
        /// </summary>
        private readonly Queue<(T Value, TransactionOutcome Outcome)> pending = new();

        /// <summary>The one delegate that each update posts.</summary>
        private readonly Action refresh;

        /// <summary>
        ///     The last value that the binding engine saw. Only the binding thread reads
        ///     and writes it. Call <see cref="ResolveInitialValue" /> before each use.
        /// </summary>
        private T cachedValue;

        /// <summary>
        ///     The sample from the constructor, until the first use of the cached value.
        ///     Then it is null. See <see cref="SampleAtCloseAndListenToUpdates{T}" /> for
        ///     the cause.
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
            this.refresh = this.Refresh;

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
        ///     thread, because the callback runs in a transaction. A binding engine that
        ///     responds immediately can come back into the graph from a callback.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The callback runs while the transaction propagates, and the transaction
        ///         can fail after it. A failure during propagation discards the update of
        ///         the cell, thus the value here is not the value of the cell then. The
        ///         posted refresh asks the transaction for its outcome, and uses the value
        ///         here only when the outcome is known as committed. In each other state
        ///         it reads the cell. See <see cref="TransactionOutcome" /> for why the
        ///         read is correct in each condition, also where a scheduler runs the
        ///         action before the transaction ends.
        ///     </para>
        ///     <para>
        ///         The value and the outcome go into the queue, and the post is the one
        ///         delegate that this object keeps. Each refresh removes the first pair, thus
        ///         a sequence of updates reaches the binding thread one at a time, as before.
        ///         Before, each update captured the two in a closure, which was two objects
        ///         for each change of each bound property. The queue allocates only when it
        ///         grows, and the two locks are short. A field with the newest pair alone
        ///         is not sufficient. The refresh of an earlier update then shows a newer
        ///         value, and the tests on a sequence of updates hold the order.
        ///     </para>
        /// </remarks>
        private void OnSourceChanged(T newValue)
        {
            TransactionOutcome outcome = TransactionInternal.CurrentOutcome();

            lock (this.pending)
            {
                this.pending.Enqueue((newValue, outcome));
            }

            this.Scheduler.Post(this.refresh);
        }

        private void Refresh()
        {
            T newValue;
            TransactionOutcome outcome;

            // The removal from the queue comes before the test of the disposal. Each post has
            // one pair, and a refresh that leaves its pair in the queue gives it to the next.
            lock (this.pending)
            {
                (newValue, outcome) = this.pending.Dequeue();
            }

            if (this.IsDisposed)
            {
                return;
            }

            this.ResolveInitialValue();
            T authoritative = outcome.Committed ? newValue : this.Cell.SampleImpl();

            if (this.comparer.Equals(x: this.cachedValue, y: authoritative))
            {
                return;
            }

            this.cachedValue = authoritative;
            this.RaiseValueChanged();
        }

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
