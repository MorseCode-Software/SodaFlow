using System;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>The commands that the factory builds for the C# surface.</summary>
internal static class BindableActions
{
    /// <summary>
    ///     A command with no parameter discards its parameter. Thus, the type of that
    ///     parameter cannot be incorrect.
    /// </summary>
    private static void AcceptAnyParameter(object? value)
    {
    }

    private static Unit UnitParameter(object? value) => Unit.Value;

    internal sealed class LoopedBindableAction
        : BindableCoreExtensionMethods.LoopedBindableAction<Unit>, ILoopedBindableAction
    {
        internal LoopedBindableAction(
            StreamSink<Unit> firingsStreamSink,
            LoopedCell<bool> isEnabledCell,
            IBindingScheduler scheduler)
            : base(
                firingsStreamSink: firingsStreamSink,
                isEnabledCell: isEnabledCell,
                scheduler: scheduler,
                validateParameter: AcceptAnyParameter,
                convertParameter: UnitParameter)
        {
        }
    }

    internal sealed class BindableAction : BindableCoreExtensionMethods.BindableAction<Unit>, IBindableAction
    {
        internal BindableAction(StreamSink<Unit> firingsStreamSink, Cell<bool> isEnabledCell, IBindingScheduler scheduler)
            : base(
                firingsStreamSink: firingsStreamSink,
                isEnabledCell: isEnabledCell,
                scheduler: scheduler,
                validateParameter: AcceptAnyParameter,
                convertParameter: UnitParameter)
        {
        }
    }

    /// <summary>
    ///     The rule for a command with an optional parameter: the
    ///     <c>CommandParameter</c> can be null, a <typeparamref name="T" />, or a
    ///     <see cref="Maybe{T}" />.
    /// </summary>
    internal static class MaybeParameter<T>
        where T : notnull
    {
        /// <exception cref="InvalidOperationException">
        ///     <paramref name="value" /> is not null, not a <typeparamref name="T" />, and
        ///     not a <see cref="Maybe{T}" />.
        /// </exception>
        internal static void Validate(object? value)
        {
            if (value is null or T or Maybe<T>)
            {
                return;
            }

            throw GetInvalidTypeException();
        }

        /// <summary>
        ///     <see cref="Validate" /> accepted the value before this point, thus the
        ///     conversion cannot fail.
        /// </summary>
        internal static Maybe<T> Convert(object? value) =>
            value switch
            {
                Maybe<T> typedMaybeValue => typedMaybeValue,
                T typedValue => Maybe.Some(typedValue),
                null => Maybe.None,
                _ => throw GetInvalidTypeException()
            };

        private static InvalidOperationException GetInvalidTypeException() =>
            new(
                "The command parameter must be of type "
                + typeof(T).FullName
                + ", "
                + typeof(Maybe<T>).FullName
                + ", or null.");
    }
}
