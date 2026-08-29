namespace SodaFlow.Bindable.ObjectModel

open SodaFlow
open SodaFlow.Bindable.ObjectModel

type IBindableAction =
    inherit IBindableAction<unit>

module Bindable =
    type internal BindableAction(firingsStreamSink, ?isEnabledCell : Cell<bool>, ?scheduler : IBindingScheduler) =
        inherit BindableCoreExtensionMethods.BindableAction<unit>(
            firingsStreamSink,
            isEnabledCell |> Option.defaultValue null,
            scheduler |> Option.defaultValue null)
        
        // A parameterless command ignores its parameter, so nothing can be mistyped.
        override this.ValidateParameter(_) = ()

        override this.SendValue(streamSink, _) = streamSink.SendImpl()
        
        interface IBindableAction

    let oneWay cell = BindableCoreExtensionMethods.ToOneWayImpl cell
    let oneWayWithComparer cell comparer= BindableCoreExtensionMethods.ToOneWayImpl(cell, comparer = comparer)
    let oneWayWithScheduler cell scheduler= BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler = scheduler)
    let oneWayWithComparerAndScheduler cell comparer scheduler= BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler, comparer)

    let twoWay cell editsStreamSink = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink)
    let twoWayWithComparer cell editsStreamSink comparer = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink, comparer = comparer)
    let twoWayWithScheduler cell editsStreamSink scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink = editsStreamSink, scheduler = scheduler)
    let twoWayWithComparerAndScheduler cell editsStreamSink comparer scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink, scheduler, comparer)

    let twoWayCS cellSink = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink)
    let twoWayCSWithComparer cellSink comparer = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, comparer = comparer)
    let twoWayCSWithScheduler cellSink scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, scheduler = scheduler)
    let twoWayCSWithComparerAndScheduler cellSink comparer scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, scheduler, comparer)

    let oneWayToSource editsStreamSink initialValue = BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue)
    let oneWayToSourceWithComparer editsStreamSink initialValue comparer= BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue, comparer)

    let oneWayToSourceCS cellSink = BindableCoreExtensionMethods.ToOneWayToSourceImpl cellSink
    let oneWayToSourceCSWithComparer cellSink comparer= BindableCoreExtensionMethods.ToOneWayToSourceImpl(cellSink, comparer)

    let toBindableActionWithValue firingsStreamSink = BindableCoreExtensionMethods.ToBindableActionImpl firingsStreamSink
    let toBindableActionWithValueAndIsEnabledCell firingsStreamSink isEnabledCell = BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, isEnabledCell)
    let toBindableActionWithValueAndScheduler firingsStreamSink scheduler = BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, scheduler = scheduler)
    let toBindableActionWithValueAndIsEnabledCellAndScheduler firingsStreamSink isEnabledCell scheduler = BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, isEnabledCell, scheduler)

    let toBindableAction firingsStreamSink : IBindableAction = new BindableAction(firingsStreamSink)
    let toBindableActionAndIsEnabledCell firingsStreamSink isEnabledCell : IBindableAction = new BindableAction(firingsStreamSink, isEnabledCell)
    let toBindableActionAndScheduler firingsStreamSink scheduler : IBindableAction = new BindableAction(firingsStreamSink, scheduler = scheduler)
    let toBindableActionAndIsEnabledCellAndScheduler firingsStreamSink isEnabledCell scheduler : IBindableAction = new BindableAction(firingsStreamSink, isEnabledCell, scheduler)