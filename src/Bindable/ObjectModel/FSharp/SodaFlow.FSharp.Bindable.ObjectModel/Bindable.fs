namespace SodaFlow.Bindable.ObjectModel

open System
open SodaFlow
open SodaFlow.Bindable.ObjectModel

type IBindableAction =
    inherit IBindableAction<unit>

/// The commands that the factory builds for the F# surface.
module internal Bindable =
    type internal BindableAction(firingsStreamSink, isEnabledCell: Cell<bool> option, scheduler: IBindingScheduler) =
        inherit
            BindableCoreExtensionMethods.BindableAction<unit>(
                firingsStreamSink,
                isEnabledCell |> Option.defaultValue null,
                scheduler
            )

        // A parameterless command ignores its parameter, so nothing can be mistyped.
        override this.ValidateParameter _ = ()

        override this.SendValue(streamSink, _) = streamSink.SendImpl()

        interface IBindableAction

    type internal BindableOptionAction<'T>(firingsStreamSink, isEnabledCell: Cell<bool> option, scheduler: IBindingScheduler)
        =
        inherit
            BindableCoreExtensionMethods.BindableAction<'T option>(
                firingsStreamSink,
                isEnabledCell |> Option.defaultValue null,
                scheduler
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
