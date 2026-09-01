using System;

namespace SodaFlow
{
    /// <summary>
    ///     A forward reference for a <see cref="Behavior{T}" /> equivalent to the <see cref="Behavior{T}" /> that is
    ///     referenced.
    /// </summary>
    /// <typeparam name="T">The type of values in the behavior loop.</typeparam>
    public class LoopedBehavior<T> : Behavior<T>
    {
        private readonly LoopedStream<T> streamLoop;

        private Lazy<T> lazyInitialValue;

        internal LoopedBehavior()
            : this(new LoopedStream<T>())
        {
        }

        private LoopedBehavior(LoopedStream<T> streamLoop)
            : base(stream: streamLoop, initialValue: default) =>
            this.streamLoop = streamLoop;

        internal void Loop(TransactionInternal trans, Behavior<T> b)
        {
            this.streamLoop.Loop(trans: trans, stream: b.Updates());
            this.lazyInitialValue = b.SampleLazy(trans);
        }

        /// <summary>
        ///     Releases the deferred initial value once this behavior has a value of its own.
        /// </summary>
        /// <remarks>
        ///     A looped behavior takes its initial value lazily from whatever the loop is closed with,
        ///     since that is not known when the loop is created. Once a value has been assigned, that
        ///     deferred value can never be needed again, so it is dropped rather than kept alive for the
        ///     lifetime of the behavior.
        /// </remarks>
        protected override void NotUsingInitialValue()
        {
            base.NotUsingInitialValue();

            this.lazyInitialValue = null;
        }

        internal override T SampleNoTransaction()
        {
            if (!this.streamLoop.IsAssigned)
            {
                throw new InvalidOperationException("BehaviorLoop was sampled before it was looped.");
            }

            this.EnsureValueIsCreated();

            return this.ValueProperty;
        }

        private void EnsureValueIsCreated()
        {
            if (this.UsingInitialValue && this.lazyInitialValue != null)
            {
                this.ValueProperty = this.lazyInitialValue.Value;
                this.lazyInitialValue = null;
            }
        }
    }
}