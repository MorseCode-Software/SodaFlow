using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel
{
    /// <summary>
    ///     Extension methods to obtain a bindable. Every implementation is a private nested type, so the
    ///     public surface is the four interfaces and nothing else.
    /// </summary>
    public static partial class BindableCoreExtensionMethods
    {
        /// <summary>Exposes a cell as a read-only bindable property.</summary>
        internal static IOneWayBindableValue<T> ToOneWayImpl<T>(
            this Cell<T> cell,
            IBindingScheduler? scheduler = null,
            IEqualityComparer<T>? comparer = null) =>
            new OneWayBindableValue<T>(cell: cell, scheduler: scheduler, comparer: comparer);

        /// <summary>
        ///     Exposes a cell sink as a two-way bindable property. The simplest case: the view is the
        ///     only writer, and the sink is the authoritative value.
        /// </summary>
        internal static ITwoWayBindableValue<T> ToTwoWayImpl<T>(
            this CellSink<T> sink,
            IBindingScheduler? scheduler = null,
            IEqualityComparer<T>? comparer = null) =>
            new TwoWayBindableValue<T>(cell: sink, write: sink.SendImpl, scheduler: scheduler, comparer: comparer);

        /// <summary>
        ///     Exposes a cell as a two-way bindable property, routing view writes into
        ///     <paramref name="editsStreamSink" />.
        /// </summary>
        internal static ITwoWayBindableValue<T> ToTwoWayImpl<T>(
            this Cell<T> cell,
            StreamSink<T> editsStreamSink,
            IBindingScheduler? scheduler = null,
            IEqualityComparer<T>? comparer = null)
        {
            if (editsStreamSink == null)
            {
                throw new ArgumentNullException(nameof(editsStreamSink));
            }

            return new TwoWayBindableValue<T>(
                cell: cell,
                write: editsStreamSink.SendImpl,
                scheduler: scheduler,
                comparer: comparer);
        }

        /// <summary>
        ///     Creates a one-way-to-source bindable property with an initial value, routing view writes into
        ///     <paramref name="sink" />.
        /// </summary>
        internal static IOneWayToSourceBindableValue<T> ToOneWayToSourceImpl<T>(
            this CellSink<T> sink,
            IEqualityComparer<T>? comparer = null) =>
            new OneWayToSourceBindableValue<T>(
                write: sink.SendImpl,
                initialValue: sink.SampleImpl(),
                comparer: comparer);

        /// <summary>
        ///     Creates a one-way-to-source bindable property with an initial value, routing view writes into
        ///     <paramref name="editsStreamSink" />.
        /// </summary>
        internal static IOneWayToSourceBindableValue<T> ToOneWayToSourceImpl<T>(
            this StreamSink<T> editsStreamSink,
            T initialValue,
            IEqualityComparer<T>? comparer = null) =>
            new OneWayToSourceBindableValue<T>(
                write: editsStreamSink.SendImpl,
                initialValue: initialValue,
                comparer: comparer);

        /// <summary>
        ///     Exposes an existing sink as a command that carries its <c>CommandParameter</c>.
        /// </summary>
        /// <remarks>
        ///     For a <c>StreamSink&lt;Unit&gt;</c> the non-generic overload wins overload resolution.
        ///     Write <c>ToBindableAction&lt;Unit&gt;(...)</c> explicitly if you want the parameterized
        ///     form for a unit sink.
        /// </remarks>
        internal static IBindableAction<T> ToBindableActionImpl<T>(
            this StreamSink<T> firingsStreamSink,
            Cell<bool>? isEnabledCell = null,
            IBindingScheduler? scheduler = null) =>
            new BindableAction<T>(
                firingsStreamSink: firingsStreamSink,
                isEnabledCell: isEnabledCell,
                scheduler: scheduler);

        /// <summary>
        ///     Subscribes to a cell's updates only — the initial value is excluded, because callers
        ///     sample it separately inside the same transaction.
        /// </summary>
        /// <remarks>
        ///     Weak by design. The node holds the handler weakly, so the returned listener is the only
        ///     thing keeping the subscription alive: callers MUST store it in a field, and it must not
        ///     be dropped as an apparently-unused local. In exchange, a bindable that becomes
        ///     unreachable without being disposed is collected along with its subscription rather than
        ///     being rooted for the lifetime of the sink it listens to. No separate reference to the
        ///     cell is required — the listener references the stream it listens to, which keeps its own
        ///     upstream dependencies alive.
        /// </remarks>
        private static IListener ListenToUpdates<T>(Cell<T> cell, Action<T> handler) =>
            cell.UpdatesImpl.ListenImpl(handler);

        /// <summary>
        ///     Sends a value into the graph after the current <see cref="TransactionInternal" /> ends so
        ///     that it can never execute inside a Sodium callback.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         With no transaction in flight — the normal case, a binding setter called from an idle
        ///         dispatcher — this opens a transaction and runs immediately, so behavior is unchanged.
        ///         With a transaction in flight, which can only happen if a nested message pump delivered
        ///         the write from inside a callback, it runs in a new transaction opened once the current
        ///         one ends. Either way the send is legal. Never call a sink's <c>Send</c> directly from a
        ///         binding setter.
        ///     </para>
        ///     <para>
        ///         Multiple posted callbacks run as separate transactions, so two writes can never collide
        ///         within one — which is why a plain <c>StreamSink&lt;T&gt;</c> works as a write target and
        ///         no coalescing variant is needed.
        ///     </para>
        /// </remarks>
        private static void PostWrite(Action write) => TransactionInternal.PostImpl(write);
    }
}
