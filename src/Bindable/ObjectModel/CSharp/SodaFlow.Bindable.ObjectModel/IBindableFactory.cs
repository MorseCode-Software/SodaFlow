using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Factory methods that give you a bindable. A caller can supply an
///     <see cref="IBindingScheduler" /> through the constructor, thus a test can replace it.
/// </summary>
[PublicAPI]
public interface IBindableFactory
{
    /// <summary>Shows a cell as a bindable property that a caller cannot write.</summary>
    IOneWayBindableValue<T> CreateOneWay<T>(Cell<T> cell, IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Shows a cell as a two-way bindable property, and sends the writes of the view into
    ///     <paramref name="editsStreamSink" />.
    /// </summary>
    ITwoWayBindableValue<T> CreateTwoWay<T>(
        Cell<T> cell,
        StreamSink<T> editsStreamSink,
        IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Shows a cell sink as a two-way bindable property. This is the simplest condition. The
    ///     view is the only writer, and the sink holds the value that is the authority.
    /// </summary>
    ITwoWayBindableValue<T> CreateTwoWay<T>(CellSink<T> sink, IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Creates a one-way-to-source bindable property with an initial value, and sends the
    ///     writes of the view into <paramref name="editsStreamSink" />.
    /// </summary>
    IOneWayToSourceBindableValue<T> CreateOneWayToSource<T>(
        StreamSink<T> editsStreamSink,
        T initialValue,
        IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Creates a one-way-to-source bindable property with an initial value, and sends the
    ///     writes of the view into <paramref name="sink" />.
    /// </summary>
    IOneWayToSourceBindableValue<T> CreateOneWayToSource<T>(
        CellSink<T> sink,
        IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Shows a sink that exists as a command that carries its <c>CommandParameter</c>.
    /// </summary>
    /// <remarks>
    ///     For a <c>StreamSink&lt;Unit&gt;</c> the compiler selects the overload with no type
    ///     parameter. To get the overload with a parameter for a unit sink, write
    ///     <c>ToBindableAction&lt;Unit&gt;(...)</c>.
    /// </remarks>
    IBindableAction<T> CreateBindableAction<T>(StreamSink<T> firingsStreamSink, Cell<bool>? isEnabledCell = null)
        where T : notnull;

    /// <summary>
    ///     Shows a sink that exists as a command with no parameter. Use this when you build the
    ///     graph on the sink and then attach the command to it.
    /// </summary>
    IBindableAction CreateBindableAction(StreamSink<Unit> firingsStreamSink, Cell<bool>? isEnabledCell = null);

    /// <summary>
    ///     Shows a sink that exists as a command with an optional parameter. The
    ///     <c>CommandParameter</c> can be <see langword="null" />, an object of type
    ///     <typeparamref name="T" />, or a <see cref="Maybe{T}" />.
    /// </summary>
    IBindableAction<Maybe<T>> CreateBindableAction<T>(
        StreamSink<Maybe<T>> firingsStreamSink,
        Cell<bool>? isEnabledCell = null)
        where T : notnull;
}
