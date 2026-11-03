namespace SodaFlow.Bindable.ObjectModel

open System
open System.Collections.Generic
open SodaFlow

/// Each method has an overload for a Cell<'T> and one for a LoopedCell<'T>. The plain one gives
/// the interface that also has the plain cell, and the looped one gives its base interface, whose
/// cell is looped. Thus, a bindable built in a loop block from a cell whose loop is not closed
/// says so. A bindable built from a plain cell does not change. The enablement cell of a
/// command is not an optional parameter, because F# cannot select between an optional Cell<bool>
/// and a LoopedCell<bool>. The overload with no cell is the command that is always enabled.
type IBindableFactory =
    abstract ToOneWay<'T> : cell: Cell<'T> * ?comparer: IEqualityComparer<'T> -> IOneWayBindableValue<'T>

    abstract ToOneWay<'T> :
        cell: LoopedCell<'T> * ?comparer: IEqualityComparer<'T> -> ILoopedOneWayBindableValue<'T>

    abstract ToTwoWay<'T> :
        cell: Cell<'T> * editsStreamSink: StreamSink<'T> * ?comparer: IEqualityComparer<'T> ->
            ITwoWayBindableValue<'T>

    abstract ToTwoWay<'T> :
        cell: LoopedCell<'T> * editsStreamSink: StreamSink<'T> * ?comparer: IEqualityComparer<'T> ->
            ILoopedTwoWayBindableValue<'T>

    abstract ToTwoWay<'T> : sink: CellSink<'T> * ?comparer: IEqualityComparer<'T> -> ITwoWayBindableValue<'T>

    abstract ToBindableAction<'T> : firingsStreamSink: StreamSink<'T> -> IBindableAction<'T>

    abstract ToBindableAction<'T> :
        firingsStreamSink: StreamSink<'T> * isEnabledCell: Cell<bool> -> IBindableAction<'T>

    abstract ToBindableAction<'T> :
        firingsStreamSink: StreamSink<'T> * isEnabledCell: LoopedCell<bool> -> ILoopedBindableAction<'T>

    abstract ToBindableAction: firingsStreamSink: StreamSink<unit> -> IBindableAction

    abstract ToBindableAction: firingsStreamSink: StreamSink<unit> * isEnabledCell: Cell<bool> -> IBindableAction

    abstract ToBindableAction:
        firingsStreamSink: StreamSink<unit> * isEnabledCell: LoopedCell<bool> -> ILoopedBindableAction

    /// A command whose CommandParameter can be null, a 'T, or a 'T option.
    abstract ToBindableOptionAction<'T> : firingsStreamSink: StreamSink<'T option> -> IBindableAction<'T option>

    abstract ToBindableOptionAction<'T> :
        firingsStreamSink: StreamSink<'T option> * isEnabledCell: Cell<bool> -> IBindableAction<'T option>

    abstract ToBindableOptionAction<'T> :
        firingsStreamSink: StreamSink<'T option> * isEnabledCell: LoopedCell<bool> -> ILoopedBindableAction<'T option>

/// The one way to build a bindable. It holds one scheduler and gives that scheduler to each
/// object that it creates. An application captures the scheduler on the UI thread at its start,
/// with SynchronizationContextBindingScheduler.Capture, and gives the factory to each view model.
/// A test supplies BindingScheduler.Immediate.
type BindableFactory(scheduler: IBindingScheduler) =
    do
        if isNull (box scheduler) then
            raise (ArgumentNullException(nameof scheduler))

    member private this.Factory = this :> IBindableFactory

    interface IBindableFactory with
        member this.ToOneWay(cell: Cell<'T>, comparer: IEqualityComparer<'T> option) =
            BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler, comparer |> Option.defaultValue null)

        member this.ToOneWay(cell: LoopedCell<'T>, comparer: IEqualityComparer<'T> option) =
            BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler, comparer |> Option.defaultValue null)

        member this.ToTwoWay(cell: Cell<'T>, editsStreamSink: StreamSink<'T>, comparer: IEqualityComparer<'T> option) =
            BindableCoreExtensionMethods.ToTwoWayImpl(
                cell,
                editsStreamSink,
                scheduler,
                comparer |> Option.defaultValue null
            )

        member this.ToTwoWay
            (cell: LoopedCell<'T>, editsStreamSink: StreamSink<'T>, comparer: IEqualityComparer<'T> option)
            =
            BindableCoreExtensionMethods.ToTwoWayImpl(
                cell,
                editsStreamSink,
                scheduler,
                comparer |> Option.defaultValue null
            )

        member this.ToTwoWay(sink: CellSink<'T>, comparer: IEqualityComparer<'T> option) =
            BindableCoreExtensionMethods.ToTwoWayImpl(sink, scheduler, comparer |> Option.defaultValue null)

        member this.ToBindableAction(firingsStreamSink: StreamSink<'T>) =
            this.Factory.ToBindableAction(firingsStreamSink, Cell.constant true)

        member this.ToBindableAction(firingsStreamSink: StreamSink<'T>, isEnabledCell: Cell<bool>) =
            BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, scheduler, isEnabledCell)

        member this.ToBindableAction(firingsStreamSink: StreamSink<'T>, isEnabledCell: LoopedCell<bool>) =
            BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, scheduler, isEnabledCell)

        member this.ToBindableAction(firingsStreamSink: StreamSink<unit>) =
            this.Factory.ToBindableAction(firingsStreamSink, Cell.constant true)

        member this.ToBindableAction(firingsStreamSink: StreamSink<unit>, isEnabledCell: Cell<bool>) =
            new Bindable.BindableAction(firingsStreamSink, isEnabledCell, scheduler) :> IBindableAction

        member this.ToBindableAction(firingsStreamSink: StreamSink<unit>, isEnabledCell: LoopedCell<bool>) =
            new Bindable.LoopedBindableAction(firingsStreamSink, isEnabledCell, scheduler) :> ILoopedBindableAction

        member this.ToBindableOptionAction(firingsStreamSink: StreamSink<'T option>) =
            this.Factory.ToBindableOptionAction(firingsStreamSink, Cell.constant true)

        member this.ToBindableOptionAction(firingsStreamSink: StreamSink<'T option>, isEnabledCell: Cell<bool>) =
            new BindableCoreExtensionMethods.BindableAction<'T option>(
                firingsStreamSink,
                isEnabledCell,
                scheduler,
                Bindable.OptionParameter.validate<'T>,
                Bindable.OptionParameter.convert<'T>
            )
            :> IBindableAction<'T option>

        member this.ToBindableOptionAction(firingsStreamSink: StreamSink<'T option>, isEnabledCell: LoopedCell<bool>) =
            new BindableCoreExtensionMethods.LoopedBindableAction<'T option>(
                firingsStreamSink,
                isEnabledCell,
                scheduler,
                Bindable.OptionParameter.validate<'T>,
                Bindable.OptionParameter.convert<'T>
            )
            :> ILoopedBindableAction<'T option>
