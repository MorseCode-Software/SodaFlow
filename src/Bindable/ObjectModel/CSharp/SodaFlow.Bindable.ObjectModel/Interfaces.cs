using System.Collections.Generic;
using System.Windows.Input;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel
{
    /// <summary>
    ///     Factory methods to obtain a bindable. A <see cref="IBindingScheduler" /> may be injected so
    ///     that it can be mocked through constructor injection.
    /// </summary>
    public interface IBindableFactory
    {
        /// <summary>Exposes a cell as a read-only bindable property.</summary>
        IOneWayBindableValue<T> ToOneWay<T>(Cell<T> cell, IEqualityComparer<T>? comparer = null);

        /// <summary>
        ///     Exposes a cell as a two-way bindable property, routing view writes into
        ///     <paramref name="editsStreamSink" />.
        /// </summary>
        ITwoWayBindableValue<T> ToTwoWay<T>(
            Cell<T> cell,
            StreamSink<T> editsStreamSink,
            IEqualityComparer<T>? comparer = null);

        /// <summary>
        ///     Exposes a cell sink as a two-way bindable property. The simplest case: the view is the
        ///     only writer, and the sink is the authoritative value.
        /// </summary>
        ITwoWayBindableValue<T> ToTwoWay<T>(CellSink<T> sink, IEqualityComparer<T>? comparer = null);

        /// <summary>
        ///     Creates a one-way-to-source bindable property with an initial value, routing view writes into
        ///     <paramref name="editsStreamSink" />.
        /// </summary>
        IOneWayToSourceBindableValue<T> ToOneWayToSource<T>(
            StreamSink<T> editsStreamSink,
            T initialValue,
            IEqualityComparer<T>? comparer = null);

        /// <summary>
        ///     Creates a one-way-to-source bindable property with an initial value, routing view writes into
        ///     <paramref name="sink" />.
        /// </summary>
        IOneWayToSourceBindableValue<T> ToOneWayToSource<T>(
            CellSink<T> sink,
            IEqualityComparer<T>? comparer = null);

        /// <summary>
        ///     Exposes an existing sink as a command that carries its <c>CommandParameter</c>.
        /// </summary>
        /// <remarks>
        ///     For a <c>StreamSink&lt;Unit&gt;</c> the non-generic overload wins overload resolution.
        ///     Write <c>ToBindableAction&lt;Unit&gt;(...)</c> explicitly if you want the parameterized
        ///     form for a unit sink.
        /// </remarks>
        IBindableAction<T> ToBindableAction<T>(StreamSink<T> firingsStreamSink, Cell<bool>? isEnabledCell = null);

        /// <summary>
        ///     Exposes an existing sink as a parameterless command. Use when the graph is built around
        ///     the sink and the command is being attached to it, rather than the other way round.
        /// </summary>
        IBindableAction ToBindableAction(StreamSink<Unit> firingsStreamSink, Cell<bool>? isEnabledCell = null);
    }

    /// <summary>
    ///     An <see cref="ICommand" /> whose invocations are exposed as a stream and whose enablement is
    ///     driven by a <see cref="Cell{T}" /> of <see cref="bool" />.
    /// </summary>
    public interface IBindableAction : IBindableAction<Unit>
    {
    }
}
