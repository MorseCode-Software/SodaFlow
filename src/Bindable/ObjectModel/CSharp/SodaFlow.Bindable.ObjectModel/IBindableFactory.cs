using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Factory methods that give you a bindable. A caller can supply an
///     <see cref="IBindingScheduler" /> through the constructor, thus a test can
///     replace it.
/// </summary>
/// <remarks>
///     Each method has an overload for a <see cref="Cell{T}" /> and one for a
///     <see cref="LoopedCell{T}" />. The plain one gives the interface that also has
///     the plain cell, and the looped one gives its base interface, whose cell is
///     looped. Thus, a bindable built in a loop block from a cell whose loop is not
///     closed says so. A bindable built from a plain cell does not change. The
///     compiler selects the plain overload for a <see cref="Cell{T}" />, as it
///     selects the plain <c>Map</c>.
/// </remarks>
[PublicAPI]
public interface IBindableFactory
{
    /// <summary>Shows a cell as a bindable property that a caller cannot write.</summary>
    IOneWayBindableValue<T> CreateOneWay<T>(Cell<T> cell, IEqualityComparer<T>? comparer = null);

    /// <inheritdoc cref="CreateOneWay{T}(Cell{T}, IEqualityComparer{T})" />
    ILoopedOneWayBindableValue<T> CreateOneWay<T>(LoopedCell<T> cell, IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Shows a cell as a two-way bindable property, and sends the writes of the
    ///     view into <paramref name="editsStreamSink" />.
    /// </summary>
    ITwoWayBindableValue<T> CreateTwoWay<T>(
        Cell<T> cell,
        StreamSink<T> editsStreamSink,
        IEqualityComparer<T>? comparer = null);

    /// <inheritdoc cref="CreateTwoWay{T}(Cell{T}, StreamSink{T}, IEqualityComparer{T})" />
    ILoopedTwoWayBindableValue<T> CreateTwoWay<T>(
        LoopedCell<T> cell,
        StreamSink<T> editsStreamSink,
        IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Shows a cell sink as a two-way bindable property. This is the simplest
    ///     condition. The view is the only writer, and the sink holds the value that
    ///     is the authority.
    /// </summary>
    ITwoWayBindableValue<T> CreateTwoWay<T>(CellSink<T> sink, IEqualityComparer<T>? comparer = null);

    /// <summary>
    ///     Shows a sink that exists as a command that carries its
    ///     <c>CommandParameter</c>.
    /// </summary>
    /// <remarks>
    ///     For a <c>StreamSink&lt;Unit&gt;</c> the compiler selects the overload with
    ///     no type parameter, whose command ignores its <c>CommandParameter</c>. That
    ///     is the correct one: a command from this overload with <c>Unit</c> as
    ///     <typeparamref name="T" /> accepts only a <c>Unit</c> as its parameter, and
    ///     no binding supplies one.
    /// </remarks>
    IBindableAction<T> CreateBindableAction<T>(StreamSink<T> firingsStreamSink, Cell<bool>? isEnabledCell = null)
        where T : notnull;

    /// <inheritdoc cref="CreateBindableAction{T}(StreamSink{T}, Cell{bool})" />
    ILoopedBindableAction<T> CreateBindableAction<T>(StreamSink<T> firingsStreamSink, LoopedCell<bool> isEnabledCell)
        where T : notnull;

    /// <summary>
    ///     Shows a sink that exists as a command with no parameter. Use this when you
    ///     build the graph on the sink and then attach the command to it.
    /// </summary>
    IBindableAction CreateBindableAction(StreamSink<Unit> firingsStreamSink, Cell<bool>? isEnabledCell = null);

    /// <inheritdoc cref="CreateBindableAction(StreamSink{Unit}, Cell{bool})" />
    ILoopedBindableAction CreateBindableAction(StreamSink<Unit> firingsStreamSink, LoopedCell<bool> isEnabledCell);

    /// <summary>
    ///     Shows a sink that exists as a command with an optional parameter. The
    ///     <c>CommandParameter</c> can be <see langword="null" />, an object of type
    ///     <typeparamref name="T" />, or a <see cref="Maybe{T}" />.
    /// </summary>
    IBindableAction<Maybe<T>> CreateBindableAction<T>(
        StreamSink<Maybe<T>> firingsStreamSink,
        Cell<bool>? isEnabledCell = null)
        where T : notnull;

    /// <inheritdoc cref="CreateBindableAction{T}(StreamSink{Maybe{T}}, Cell{bool})" />
    ILoopedBindableAction<Maybe<T>> CreateBindableAction<T>(
        StreamSink<Maybe<T>> firingsStreamSink,
        LoopedCell<bool> isEnabledCell)
        where T : notnull;
}
