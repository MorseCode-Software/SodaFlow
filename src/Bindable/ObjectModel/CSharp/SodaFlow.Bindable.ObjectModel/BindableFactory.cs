using System.Collections.Generic;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     The default <see cref="IBindableFactory" />. Holds one scheduler and hands it to everything
///     it creates, so a view model can take the factory through its constructor and a test can
///     substitute <see cref="BindingScheduler.Immediate" /> for the real one.
/// </summary>
public class BindableFactory : IBindableFactory
{
    private readonly IBindingScheduler? bindingScheduler;

    /// <summary>
    ///     Initializes a new instance which builds bindables against the given scheduler.
    /// </summary>
    /// <param name="bindingScheduler">
    ///     Marshals notifications onto the binding thread. Null leaves each bindable to resolve one
    ///     ambiently, which is what an application constructing its view models on the UI thread
    ///     wants; pass one explicitly from a test.
    /// </param>
    public BindableFactory(IBindingScheduler? bindingScheduler) => this.bindingScheduler = bindingScheduler;

    /// <inheritdoc />
    public IOneWayBindableValue<T> ToOneWay<T>(Cell<T> cell, IEqualityComparer<T>? comparer = null) =>
        cell.ToOneWay(scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public ITwoWayBindableValue<T> ToTwoWay<T>(
        Cell<T> cell,
        StreamSink<T> editsStreamSink,
        IEqualityComparer<T>? comparer = null) =>
        cell.ToTwoWay(editsStreamSink: editsStreamSink, scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public ITwoWayBindableValue<T> ToTwoWay<T>(CellSink<T> sink, IEqualityComparer<T>? comparer = null) =>
        sink.ToTwoWay(scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public IOneWayToSourceBindableValue<T> ToOneWayToSource<T>(
        StreamSink<T> editsStreamSink,
        T initialValue,
        IEqualityComparer<T>? comparer = null) =>
        editsStreamSink.ToOneWayToSource(initialValue: initialValue, comparer: comparer);

    /// <inheritdoc />
    public IOneWayToSourceBindableValue<T> ToOneWayToSource<T>(
        CellSink<T> sink,
        IEqualityComparer<T>? comparer = null) =>
        sink.ToOneWayToSource(comparer);

    /// <inheritdoc />
    public IBindableAction<T> ToBindableAction<T>(
        StreamSink<T> firingsStreamSink,
        Cell<bool>? isEnabledCell = null) =>
        firingsStreamSink.ToBindableAction(
            isEnabledCell: isEnabledCell,
            scheduler: this.bindingScheduler);

    /// <inheritdoc />
    public IBindableAction ToBindableAction(StreamSink<Unit> firingsStreamSink, Cell<bool>? isEnabledCell = null) =>
        firingsStreamSink.ToBindableAction(
            isEnabledCell: isEnabledCell,
            scheduler: this.bindingScheduler);
}