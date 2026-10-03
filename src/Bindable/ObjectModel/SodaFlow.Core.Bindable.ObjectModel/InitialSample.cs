using System;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     The value of a cell when the transaction that builds a bindable closes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A bindable cannot sample its cell in its constructor. The caller can build the
    ///         bindable in a loop, and there the cell has no value until the loop closes. This
    ///         type gets the value in the sample phase of the building transaction. That phase
    ///         occurs after the loop closes.
    ///     </para>
    ///     <para>
    ///         A <see cref="Read" /> before that phase samples the cell. That occurs only when
    ///         code reads the bindable before the building transaction closes. On a different
    ///         thread, that sample waits for the transaction lock, thus it gets the value after
    ///         the close. On the building thread, it gets the value of the cell at that time. In
    ///         the loop block, it throws, as a sample of the looped cell throws there. This type
    ///         keeps no exception, thus a read after the loop closes gets the value.
    ///         <see cref="Lazy{T}" /> keeps the exception, and that is why this type is not one.
    ///     </para>
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class InitialSample<T>(in Cell<T> cell)
    {
        private readonly Cell<T> cell = cell;

        /// <summary>
        ///     This becomes true after <see cref="value" /> has its value. It is volatile, thus a
        ///     reader on a different thread that finds true also finds that value.
        /// </summary>
        private volatile bool sampled;

        // ReSharper disable once NullableWarningSuppressionIsUsed - Never read while sampled is false, and Set writes it before it sets sampled.
        private T value = default!;

        /// <summary>
        ///     Makes a sample of <paramref name="cell" /> that gets its value when
        ///     <paramref name="transaction" /> closes.
        /// </summary>
        /// <remarks>
        ///     The value comes from <c>SampleLazy</c>, which gives the value that the cell takes
        ///     in this transaction and not the previous value. Thus, an update in the building
        ///     transaction needs no correction, and causes no notification.
        /// </remarks>
        internal static InitialSample<T> Take(TransactionInternal transaction, Cell<T> cell)
        {
            InitialSample<T> sample = new(cell);
            Lazy<T> atClose = cell.SampleLazyImpl();

            transaction.Sample(() => sample.Set(atClose.Value));

            return sample;
        }

        /// <summary>
        ///     Gives the value from the sample phase, or samples the cell before that phase.
        /// </summary>
        internal T Read() => this.sampled ? this.value : this.cell.SampleImpl();

        private void Set(T sampledValue)
        {
            this.value = sampledValue;
            this.sampled = true;
        }
    }
}
