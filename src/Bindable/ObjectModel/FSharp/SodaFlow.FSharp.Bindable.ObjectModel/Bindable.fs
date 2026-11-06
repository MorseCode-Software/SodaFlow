namespace SodaFlow.Bindable.ObjectModel

open System
open SodaFlow
open SodaFlow.Bindable.ObjectModel

/// A command with no parameter, whose enablement cell can be in a loop that is not closed.
type ILoopedBindableAction =
    inherit ILoopedBindableAction<unit>

/// A command with no parameter, built from a Cell<bool>, which it gives back as one.
type IBindableAction =
    inherit IBindableAction<unit>
    inherit ILoopedBindableAction

/// The commands that the factory builds for the F# surface.
module internal Bindable =
    /// A parameterless command ignores its parameter, so nothing can be mistyped.
    let acceptAnyParameter (_: obj) = ()

    let unitParameter (_: obj) = ()

    type internal LoopedBindableAction(firingsStreamSink, isEnabledCell: LoopedCell<bool>, scheduler: IBindingScheduler)
        =
        inherit
            BindableCoreExtensionMethods.LoopedBindableAction<unit>(
                firingsStreamSink,
                isEnabledCell,
                scheduler,
                acceptAnyParameter,
                unitParameter
            )

        interface ILoopedBindableAction

    type internal BindableAction(firingsStreamSink, isEnabledCell: Cell<bool>, scheduler: IBindingScheduler) =
        inherit
            BindableCoreExtensionMethods.BindableAction<unit>(
                firingsStreamSink,
                isEnabledCell,
                scheduler,
                acceptAnyParameter,
                unitParameter
            )

        interface IBindableAction

    /// The rule for a command whose CommandParameter can be null, a 'T, or a 'T option.
    module OptionParameter =
        let private getInvalidTypeException<'T> () =
            InvalidOperationException(
                "The command parameter must be of type "
                + typeof<'T>.FullName
                + ", "
                + typeof<'T option>.FullName
                + ", or null."
            )

        let validate<'T> (value: obj) =
            match value with
            | null
            | :? 'T
            | :? ('T option) -> ()
            | _ -> getInvalidTypeException<'T> () |> raise

        let convert<'T> (value: obj) : 'T option =
            match value with
            | null -> None
            | :? 'T as value -> Some value
            | :? ('T option) as value -> value
            | _ -> getInvalidTypeException<'T> () |> raise
