using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     The entry point for a value that starts in the view. A <c>OneWayToSource</c> binding
    ///     sends the state of a control into the graph. A selection, a scroll offset, and the
    ///     focus are such state.
    /// </summary>
    /// <remarks>
    ///     You can build this on any thread. The thread that builds the instance keeps the
    ///     initial value. Only the code that publishes the instance puts that thread and the
    ///     binding thread in sequence, and that code must do this for <c>comparer</c> and
    ///     <c>write</c> in all conditions.
    ///     This class has no scheduler, because no value moves back out to the view and thus
    ///     there is nothing to move between threads. The binding engine reads and writes the
    ///     cached value on the binding thread, and no other code touches it. See
    ///     <see cref="IWritableBindableValue{T}" />.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class OneWayToSourceBindableValue<T> : IOneWayToSourceBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;

        /// <summary>
        ///     This field only tells you which thread the binding engine is on. This class posts
        ///     nothing through it, because no value moves back out to the view. Thus, it schedules
        ///     no work.
        /// </summary>
        private readonly IBindingScheduler scheduler;

        private readonly Action<T> write;

        /// <summary>
        ///     The last value that the binding engine saw. Only the binding thread reads and
        ///     writes it, which is what lets it be a usual field. See
        ///     <see cref="IWritableBindableValue{T}" /> for the cause.
        /// </summary>
        private T cachedValue;

        private int disposed;

        /// <param name="write">Gets the values that the view writes. Usually <c>sink.Send</c>.</param>
        /// <param name="initialValue">
        ///     The value that the graph sees before the view writes a value. The binding engine
        ///     usually writes the correct value during the first layout cycle.
        /// </param>
        /// <param name="comparer">
        ///     Tells you if a value changed. A null value selects the default comparer.
        /// </param>
        /// <param name="scheduler">
        ///     Identifies the binding thread. Thus, this class finds a read or a write of
        ///     <see cref="Value" /> from a different thread, and that access does not damage the
        ///     cached value without a warning. A null value selects the ambient scheduler, as it
        ///     does in the other classes.
        /// </param>
        internal OneWayToSourceBindableValue(
            Action<T> write,
            T initialValue,
            IEqualityComparer<T>? comparer,
            IBindingScheduler? scheduler)
        {
            this.comparer = comparer ?? EqualityComparer<T>.Default;
            this.scheduler = BindingScheduler.Resolve(scheduler);
            this.cachedValue = initialValue;
            this.write = write ?? throw new ArgumentNullException(nameof(write));
        }

        /// <inheritdoc />
        public T Value
        {
            get
            {
                this.scheduler.VerifyAccess("IOneWayToSourceBindableValue<T>.Value");

                return this.cachedValue;
            }

            set
            {
                this.scheduler.VerifyAccess("IOneWayToSourceBindableValue<T>.Value");

                if (Volatile.Read(ref this.disposed) != 0)
                {
                    return;
                }

                if (this.comparer.Equals(x: this.cachedValue, y: value))
                {
                    return;
                }

                this.cachedValue = value;

                // This code tests again in the post and does not depend on the test above.
                // PostWrite defers while a transaction is open. Thus, a Dispose between the two
                // can let this write reach the graph.
                PostWrite(() =>
                {
                    if (Volatile.Read(ref this.disposed) != 0)
                    {
                        return;
                    }

                    this.write(value);
                });
            }
        }

        /// <summary>
        ///     Stops the acceptance of a write. This method does not change the sink. Thus, a
        ///     listener below it continues to see the last value and does not get an error.
        /// </summary>
        // ReSharper disable once InheritdocConsiderUsage
        public void Dispose() => Interlocked.Exchange(location1: ref this.disposed, value: 1);
    }
}
