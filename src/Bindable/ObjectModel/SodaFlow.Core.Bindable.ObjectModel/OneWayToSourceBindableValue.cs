using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     The entry point for values that originate in the view: control state such as selection,
    ///     scroll offset, or focus, pushed into the graph by a <c>OneWayToSource</c> binding.
    /// </summary>
    /// <remarks>
    ///     Safe to construct on any thread. The initial value is stored by whichever thread builds
    ///     the instance and read by the binding thread; <see cref="ValueBox{T}" /> is what orders
    ///     the two.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class OneWayToSourceBindableValue<T> : IOneWayToSourceBindableValue<T>
    {
        private readonly IEqualityComparer<T> comparer;
        private readonly Action<T> write;

        /// <summary>
        ///     Boxed so the field can be volatile whatever <typeparamref name="T" /> is. See
        ///     <see cref="ValueBox{T}" />.
        /// </summary>
        private volatile ValueBox<T> box;

        private int disposed;

        /// <param name="write">Receives values written by the view. Typically <c>sink.Send</c>.</param>
        /// <param name="initialValue">
        ///     The value the graph sees before the view has written anything. The binding engine
        ///     typically writes the real value during the first layout pass.
        /// </param>
        /// <param name="comparer">
        ///     Decides whether a value has actually changed. Null uses the default comparer.
        /// </param>
        internal OneWayToSourceBindableValue(Action<T> write, T initialValue, IEqualityComparer<T>? comparer)
        {
            this.comparer = comparer ?? EqualityComparer<T>.Default;
            this.box = new ValueBox<T>(initialValue);
            this.write = write ?? throw new ArgumentNullException(nameof(write));
        }

        /// <inheritdoc />
        public T Value
        {
            get => this.box.Value;
            set
            {
                if (Volatile.Read(ref this.disposed) != 0)
                {
                    return;
                }

                if (this.comparer.Equals(x: this.box.Value, y: value))
                {
                    return;
                }

                this.box = new ValueBox<T>(value);

                // Checked again inside the post, not only above. PostWrite defers whenever a
                // transaction is already open, so a Dispose between the two would otherwise
                // still let this write reach the graph.
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
        ///     Stops accepting writes. The sink itself is left intact so downstream subscribers keep
        ///     observing the last value rather than faulting.
        /// </summary>
        // ReSharper disable once InheritdocConsiderUsage
        public void Dispose() => Interlocked.Exchange(location1: ref this.disposed, value: 1);
    }
}
