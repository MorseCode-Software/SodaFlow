namespace SodaFlow.Bindable.ObjectModel

open System
open System.Runtime.CompilerServices
open SodaFlow
open SodaFlow.Bindable.ObjectModel

type IBindableAction =
    inherit IBindableAction<unit>

module Bindable =
    type internal BindableAction(firingsStreamSink, ?isEnabledCell: Cell<bool>, ?scheduler: IBindingScheduler) =
        inherit
            BindableCoreExtensionMethods.BindableAction<unit>(
                firingsStreamSink,
                isEnabledCell |> Option.defaultValue null,
                scheduler |> Option.defaultValue null
            )

        // A parameterless command ignores its parameter, so nothing can be mistyped.
        override this.ValidateParameter _ = ()

        override this.SendValue(streamSink, _) = streamSink.SendImpl()

        interface IBindableAction

    type internal BindableOptionAction<'T>(firingsStreamSink, ?isEnabledCell: Cell<bool>, ?scheduler: IBindingScheduler)
        =
        inherit
            BindableCoreExtensionMethods.BindableAction<'T option>(
                firingsStreamSink,
                isEnabledCell |> Option.defaultValue null,
                scheduler |> Option.defaultValue null
            )

        static member private GetInvalidTypeException() =
            InvalidOperationException(
                "The command parameter must be of type "
                + typeof<'T>.FullName
                + ", "
                + typeof<'T option>.FullName
                + ", or null."
            )

        override this.ValidateParameter value =
            match value with
            | null
            | :? 'T
            | :? ('T option) -> ()
            | _ -> BindableOptionAction<'T>.GetInvalidTypeException() |> raise

        override this.SendValue(streamSink, value) =
            streamSink.SendImpl(
                match value with
                | null -> None
                | :? 'T as value -> Some value
                | :? ('T option) as value -> value
                | _ -> BindableOptionAction<'T>.GetInvalidTypeException() |> raise
            )

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWay cell =
        BindableCoreExtensionMethods.ToOneWayImpl cell

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayWithComparer cell comparer =
        BindableCoreExtensionMethods.ToOneWayImpl(cell, comparer = comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayWithScheduler cell scheduler =
        BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayWithSchedulerAndComparer cell scheduler comparer =
        BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWay cell editsStreamSink =
        BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayWithComparer cell editsStreamSink comparer =
        BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink, comparer = comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayWithScheduler cell editsStreamSink scheduler =
        BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink = editsStreamSink, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayWithSchedulerAndComparer cell editsStreamSink scheduler comparer =
        BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCS cellSink =
        BindableCoreExtensionMethods.ToTwoWayImpl(cellSink)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCSWithComparer cellSink comparer =
        BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, comparer = comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCSWithScheduler cellSink scheduler =
        BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCSWithSchedulerAndComparer cellSink scheduler comparer =
        BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSource (editsStreamSink: StreamSink<_>) initialValue =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceWithScheduler (editsStreamSink: StreamSink<_>) initialValue scheduler =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceWithComparer (editsStreamSink: StreamSink<_>) initialValue comparer =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue, comparer = comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceWithSchedulerAndComparer (editsStreamSink: StreamSink<_>) initialValue scheduler comparer =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceCS (cellSink: CellSink<'T>) =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl cellSink

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceCSWithScheduler (cellSink: CellSink<'T>) scheduler =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(cellSink, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceCSWithComparer (cellSink: CellSink<'T>) comparer =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(cellSink, comparer = comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceCSWithSchedulerAndComparer (cellSink: CellSink<'T>) scheduler comparer =
        BindableCoreExtensionMethods.ToOneWayToSourceImpl(cellSink, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValue firingsStreamSink =
        BindableCoreExtensionMethods.ToBindableActionImpl firingsStreamSink

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValueAndIsEnabledCell firingsStreamSink isEnabledCell =
        BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, isEnabledCell)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValueAndScheduler firingsStreamSink scheduler =
        BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValueAndIsEnabledCellAndScheduler firingsStreamSink isEnabledCell scheduler =
        BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, isEnabledCell, scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableAction firingsStreamSink : IBindableAction = new BindableAction(firingsStreamSink)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionAndIsEnabledCell firingsStreamSink isEnabledCell : IBindableAction =
        new BindableAction(firingsStreamSink, isEnabledCell)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionAndScheduler firingsStreamSink scheduler : IBindableAction =
        new BindableAction(firingsStreamSink, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionAndIsEnabledCellAndScheduler firingsStreamSink isEnabledCell scheduler : IBindableAction =
        new BindableAction(firingsStreamSink, isEnabledCell, scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithOptionalValue firingsStreamSink : IBindableAction<_ option> =
        new BindableOptionAction<_>(firingsStreamSink)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithOptionalValueAndIsEnabledCell firingsStreamSink isEnabledCell : IBindableAction<_ option> =
        new BindableOptionAction<_>(firingsStreamSink, isEnabledCell)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithOptionalValueAndScheduler firingsStreamSink scheduler : IBindableAction<_ option> =
        new BindableOptionAction<_>(firingsStreamSink, scheduler = scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithOptionalValueAndIsEnabledCellAndScheduler
        firingsStreamSink
        isEnabledCell
        scheduler
        : IBindableAction<_ option> =
        new BindableOptionAction<_>(firingsStreamSink, isEnabledCell, scheduler)
