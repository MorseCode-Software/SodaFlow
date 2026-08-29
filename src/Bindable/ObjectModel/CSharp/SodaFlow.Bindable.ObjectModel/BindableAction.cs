using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel
{
    public static partial class BindableExtensionMethods
    {
        /// <summary>
        ///     An <see cref="System.Windows.Input.ICommand" /> whose enablement is a
        ///     <see cref="Cell{T}" /> and whose invocations are a <see cref="Stream{T}" />.
        /// </summary>
        internal sealed class BindableAction : BindableCoreExtensionMethods.BindableAction<Unit>, IBindableAction
        {
            internal BindableAction(
                StreamSink<Unit> firingsStreamSink,
                Cell<bool>? isEnabledCell,
                IBindingScheduler? scheduler)
                : base(firingsStreamSink: firingsStreamSink, isEnabledCell: isEnabledCell, scheduler: scheduler)
            {
            }

            /// <inheritdoc />
            /// <remarks>A parameterless command ignores its parameter, so nothing can be mistyped.</remarks>
            protected override void ValidateParameter(object? value)
            {
            }

            /// <inheritdoc />
            protected override void SendValue(StreamSink<Unit> streamSink, object? value) =>
                streamSink.SendImpl(Unit.Value);
        }
    }
}