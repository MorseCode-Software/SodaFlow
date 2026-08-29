using System.Collections.Generic;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel
{
    public class BindableFactory : IBindableFactory
    {
        private readonly IBindingScheduler? bindingScheduler;

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
}