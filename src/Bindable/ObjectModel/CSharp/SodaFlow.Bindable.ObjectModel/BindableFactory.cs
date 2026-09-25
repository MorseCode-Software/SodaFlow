using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     The default <see cref="IBindableFactory" />. It holds one scheduler and gives that
///     scheduler to each object that it creates. Thus, a view model can take the factory through
///     its constructor, and a test can supply <see cref="BindingScheduler.Immediate" /> without a copy
///     of the true scheduler.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class BindableFactory : IBindableFactory
{
    private readonly IBindingScheduler bindingScheduler;

    /// <summary>
    ///     Creates an instance that builds bindable objects with the given scheduler.
    /// </summary>
    /// <param name="bindingScheduler">
    ///     Moves notifications to the binding thread. An application captures it on the UI
    ///     thread at its start, with <see cref="SynchronizationContextBindingScheduler.Capture" />.
    ///     A test supplies <see cref="BindingScheduler.Immediate" />.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="bindingScheduler" /> is null.</exception>
    public BindableFactory(IBindingScheduler bindingScheduler) =>
        this.bindingScheduler = bindingScheduler ?? throw new ArgumentNullException(nameof(bindingScheduler));

    /// <inheritdoc />
    public IOneWayBindableValue<T> CreateOneWay<T>(Cell<T> cell, IEqualityComparer<T>? comparer = null) =>
        cell.ToOneWayImpl(scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public ITwoWayBindableValue<T> CreateTwoWay<T>(
        Cell<T> cell,
        StreamSink<T> editsStreamSink,
        IEqualityComparer<T>? comparer = null) =>
        cell.ToTwoWayImpl(editsStreamSink: editsStreamSink, scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public ITwoWayBindableValue<T> CreateTwoWay<T>(CellSink<T> sink, IEqualityComparer<T>? comparer = null) =>
        sink.ToTwoWayImpl(scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public IOneWayToSourceBindableValue<T> CreateOneWayToSource<T>(
        StreamSink<T> editsStreamSink,
        T initialValue,
        IEqualityComparer<T>? comparer = null) =>
        editsStreamSink.ToOneWayToSourceImpl(
            initialValue: initialValue,
            scheduler: this.bindingScheduler,
            comparer: comparer);

    /// <inheritdoc />
    public IOneWayToSourceBindableValue<T> CreateOneWayToSource<T>(
        CellSink<T> sink,
        IEqualityComparer<T>? comparer = null) =>
        sink.ToOneWayToSourceImpl(scheduler: this.bindingScheduler, comparer: comparer);

    /// <inheritdoc />
    public IBindableAction<T> CreateBindableAction<T>(
        StreamSink<T> firingsStreamSink,
        Cell<bool>? isEnabledCell = null)
        where T : notnull =>
        firingsStreamSink.ToBindableActionImpl(scheduler: this.bindingScheduler, isEnabledCell: isEnabledCell);

    /// <inheritdoc />
    public IBindableAction CreateBindableAction(StreamSink<Unit> firingsStreamSink, Cell<bool>? isEnabledCell = null) =>
        new BindableActions.BindableAction(
            firingsStreamSink: firingsStreamSink,
            isEnabledCell: isEnabledCell,
            scheduler: this.bindingScheduler);

    /// <inheritdoc />
    public IBindableAction<Maybe<T>> CreateBindableAction<T>(
        StreamSink<Maybe<T>> firingsStreamSink,
        Cell<bool>? isEnabledCell = null)
        where T : notnull =>
        new BindableActions.BindableMaybeAction<T>(
            firingsStreamSink: firingsStreamSink,
            isEnabledCell: isEnabledCell,
            scheduler: this.bindingScheduler);
}
