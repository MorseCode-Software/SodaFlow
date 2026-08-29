namespace SodaFlow.Bindable.ObjectModel

open System.Runtime.CompilerServices
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

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWay cell = BindableCoreExtensionMethods.ToOneWayImpl cell
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayWithComparer cell comparer= BindableCoreExtensionMethods.ToOneWayImpl(cell, comparer = comparer)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayWithScheduler cell scheduler= BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler = scheduler)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayWithComparerAndScheduler cell comparer scheduler= BindableCoreExtensionMethods.ToOneWayImpl(cell, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWay cell editsStreamSink = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayWithComparer cell editsStreamSink comparer = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink, comparer = comparer)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayWithScheduler cell editsStreamSink scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink = editsStreamSink, scheduler = scheduler)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayWithComparerAndScheduler cell editsStreamSink comparer scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cell, editsStreamSink, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCS cellSink = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCSWithComparer cellSink comparer = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, comparer = comparer)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCSWithScheduler cellSink scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, scheduler = scheduler)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let twoWayCSWithComparerAndScheduler cellSink comparer scheduler = BindableCoreExtensionMethods.ToTwoWayImpl(cellSink, scheduler, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSource editsStreamSink initialValue = BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceWithComparer editsStreamSink initialValue comparer= BindableCoreExtensionMethods.ToOneWayToSourceImpl(editsStreamSink, initialValue, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceCS cellSink = BindableCoreExtensionMethods.ToOneWayToSourceImpl cellSink
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let oneWayToSourceCSWithComparer cellSink comparer= BindableCoreExtensionMethods.ToOneWayToSourceImpl(cellSink, comparer)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValue firingsStreamSink = BindableCoreExtensionMethods.ToBindableActionImpl firingsStreamSink
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValueAndIsEnabledCell firingsStreamSink isEnabledCell = BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, isEnabledCell)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValueAndScheduler firingsStreamSink scheduler = BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, scheduler = scheduler)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionWithValueAndIsEnabledCellAndScheduler firingsStreamSink isEnabledCell scheduler = BindableCoreExtensionMethods.ToBindableActionImpl(firingsStreamSink, isEnabledCell, scheduler)

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableAction firingsStreamSink : IBindableAction = new BindableAction(firingsStreamSink)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionAndIsEnabledCell firingsStreamSink isEnabledCell : IBindableAction = new BindableAction(firingsStreamSink, isEnabledCell)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionAndScheduler firingsStreamSink scheduler : IBindableAction = new BindableAction(firingsStreamSink, scheduler = scheduler)
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let toBindableActionAndIsEnabledCellAndScheduler firingsStreamSink isEnabledCell scheduler : IBindableAction = new BindableAction(firingsStreamSink, isEnabledCell, scheduler)