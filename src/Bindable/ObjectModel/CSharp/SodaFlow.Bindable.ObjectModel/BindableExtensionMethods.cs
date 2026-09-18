using System.Collections.Generic;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Extension methods that give you a bindable. Each implementation is a private nested type.
///     Thus the public surface is the four interfaces only.
/// </summary>
public static partial class BindableExtensionMethods
{
    /// <summary>Shows a cell as a bindable property that a caller cannot write.</summary>
    public static IOneWayBindableValue<T> ToOneWay<T>(
        this Cell<T> cell,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        cell.ToOneWayImpl(scheduler: scheduler, comparer: comparer);

    /// <summary>
    ///     Shows a cell sink as a two-way bindable property. This is the simplest condition. The
    ///     view is the only writer, and the sink holds the value that is the authority.
    /// </summary>
    public static ITwoWayBindableValue<T> ToTwoWay<T>(
        this CellSink<T> sink,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        sink.ToTwoWayImpl(scheduler: scheduler, comparer: comparer);

    /// <summary>
    ///     Shows a cell as a two-way bindable property, and sends the writes of the view into
    ///     <paramref name="editsStreamSink" />.
    /// </summary>
    public static ITwoWayBindableValue<T> ToTwoWay<T>(
        this Cell<T> cell,
        StreamSink<T> editsStreamSink,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        cell.ToTwoWayImpl(editsStreamSink: editsStreamSink, scheduler: scheduler, comparer: comparer);

    /// <summary>
    ///     Creates a one-way-to-source bindable property with an initial value, and sends the
    ///     writes of the view into <paramref name="editsStreamSink" />.
    /// </summary>
    public static IOneWayToSourceBindableValue<T> ToOneWayToSource<T>(
        this StreamSink<T> editsStreamSink,
        T initialValue,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        editsStreamSink.ToOneWayToSourceImpl(
            initialValue: initialValue,
            scheduler: scheduler,
            comparer: comparer);

    /// <summary>
    ///     Creates a one-way-to-source bindable property with an initial value, and sends the
    ///     writes of the view into <paramref name="sink" />.
    /// </summary>
    public static IOneWayToSourceBindableValue<T> ToOneWayToSource<T>(
        this CellSink<T> sink,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        sink.ToOneWayToSourceImpl(scheduler: scheduler, comparer: comparer);

    /// <summary>
    ///     Shows a sink that exists as a command that carries its <c>CommandParameter</c>.
    /// </summary>
    /// <remarks>
    ///     For a <c>StreamSink&lt;Unit&gt;</c> the compiler selects the overload with no type
    ///     parameter. To get the overload with a parameter for a unit sink, write
    ///     <c>ToBindableAction&lt;Unit&gt;(...)</c>.
    /// </remarks>
    public static IBindableAction<T> ToBindableAction<T>(
        this StreamSink<T> firingsStreamSink,
        Cell<bool>? isEnabledCell = null,
        IBindingScheduler? scheduler = null)
        where T : notnull =>
        firingsStreamSink.ToBindableActionImpl(isEnabledCell: isEnabledCell, scheduler: scheduler);

    /// <summary>
    ///     Shows a sink that exists as a command with no parameter. Use this when you build the
    ///     graph on the sink and then attach the command to it.
    /// </summary>
    public static IBindableAction ToBindableAction(
        this StreamSink<Unit> firingsStreamSink,
        Cell<bool>? isEnabledCell = null,
        IBindingScheduler? scheduler = null) =>
        new BindableAction(
            firingsStreamSink: firingsStreamSink,
            isEnabledCell: isEnabledCell,
            scheduler: scheduler);

    /// <summary>
    ///     Shows a sink that exists as a command with an optional parameter. The
    ///     <c>CommandParameter</c> can be <see langword="null" />, an object of type
    ///     <typeparamref name="T" />, or a <see cref="Maybe{T}" />.
    /// </summary>
    public static IBindableAction<Maybe<T>> ToBindableAction<T>(
        this StreamSink<Maybe<T>> firingsStreamSink,
        Cell<bool>? isEnabledCell = null,
        IBindingScheduler? scheduler = null)
        where T : notnull =>
        new BindableMaybeAction<T>(
            firingsStreamSink: firingsStreamSink,
            isEnabledCell: isEnabledCell,
            scheduler: scheduler);
}
