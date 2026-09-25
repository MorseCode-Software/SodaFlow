namespace SodaFlow.Bindable.ObjectModel

open System
open System.Collections.Generic
open SodaFlow

type IBindableFactory =
    abstract ToOneWay<'T> : cell: Cell<'T> * ?comparer: IEqualityComparer<'T> -> IOneWayBindableValue<'T>

    abstract ToTwoWay<'T> :
        cell: Cell<'T> * editsStreamSink: StreamSink<'T> * ?comparer: IEqualityComparer<'T> -> ITwoWayBindableValue<'T>

    abstract ToTwoWay<'T> : sink: CellSink<'T> * ?comparer: IEqualityComparer<'T> -> ITwoWayBindableValue<'T>

    abstract ToOneWayToSource<'T> :
        editsStreamSink: StreamSink<'T> * initialValue: 'T * ?comparer: IEqualityComparer<'T> ->
            IOneWayToSourceBindableValue<'T>

    abstract ToOneWayToSource<'T> :
        sink: CellSink<'T> * ?comparer: IEqualityComparer<'T> -> IOneWayToSourceBindableValue<'T>

    abstract ToBindableAction<'T> :
        firingsStreamSink: StreamSink<'T> * ?isEnabledCell: Cell<bool> -> IBindableAction<'T>

    abstract ToBindableAction: firingsStreamSink: StreamSink<unit> * ?isEnabledCell: Cell<bool> -> IBindableAction

    /// A command whose CommandParameter can be null, a 'T, or a 'T option.
    abstract ToBindableOptionAction<'T> :
        firingsStreamSink: StreamSink<'T option> * ?isEnabledCell: Cell<bool> -> IBindableAction<'T option>

/// The one way to build a bindable. It holds one scheduler and gives that scheduler to each
/// object that it creates. An application captures the scheduler on the UI thread at its start,
/// with SynchronizationContextBindingScheduler.Capture, and gives the factory to each view model.
/// A test supplies BindingScheduler.Immediate.
type BindableFactory(scheduler: IBindingScheduler) =
    do
        if isNull (box scheduler) then
            raise (ArgumentNullException(nameof scheduler))

    interface IBindableFactory with
        member this.ToOneWay(cell, comparer) =
            BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler, comparer |> Option.defaultValue null)

        member this.ToTwoWay(cell, editsStreamSink, comparer) =
            BindableCoreExtensionMethods.ToTwoWayImpl(
                cell,
                editsStreamSink,
                scheduler,
                comparer |> Option.defaultValue null
            )

        member this.ToTwoWay(sink, comparer) =
            BindableCoreExtensionMethods.ToTwoWayImpl(sink, scheduler, comparer |> Option.defaultValue null)

        member this.ToOneWayToSource(editsStreamSink, initialValue, comparer) =
            BindableCoreExtensionMethods.ToOneWayToSourceImpl(
                editsStreamSink,
                initialValue,
                scheduler,
                comparer |> Option.defaultValue null
            )

        member this.ToOneWayToSource(sink, comparer) =
            BindableCoreExtensionMethods.ToOneWayToSourceImpl(sink, scheduler, comparer |> Option.defaultValue null)

        member this.ToBindableAction(firingsStreamSink, isEnabledCell: Cell<bool> option) =
            BindableCoreExtensionMethods.ToBindableActionImpl(
                firingsStreamSink,
                scheduler,
                isEnabledCell |> Option.defaultValue null
            )

        member this.ToBindableAction(firingsStreamSink, isEnabledCell: Cell<bool> option) : IBindableAction =
            new Bindable.BindableAction(firingsStreamSink, isEnabledCell, scheduler)

        member this.ToBindableOptionAction(firingsStreamSink, isEnabledCell: Cell<bool> option) =
            new Bindable.BindableOptionAction<_>(firingsStreamSink, isEnabledCell, scheduler)
