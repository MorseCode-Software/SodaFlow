using System;
using System.Threading;
using System.Windows.Input;
using JetBrains.Annotations;

namespace SodaFlow.Bindable.ObjectModel;

[PublicAPI]
public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     An <see cref="ICommand" /> that moves its <c>CommandParameter</c> to the
    ///     stream. A <see cref="LoopedCell{T}" /> of <see cref="bool" /> controls when
    ///     the command is available.
    /// </summary>
    /// <remarks>
    ///     You can build this on any thread. The building transaction samples the
    ///     availability when it closes, and the binding thread reads that sample. The
    ///     scheduler moves each subsequent change to the binding thread.
    ///     <see cref="Dispose" /> can run on any thread, thus only atomic operations
    ///     change the field that holds the availability.
    ///     The two functions say what a <c>CommandParameter</c> can be, and how it
    ///     becomes a value of the stream. A command with no parameter accepts each
    ///     parameter and sends a unit. A command with an optional parameter accepts a
    ///     null. Thus, the two language surfaces give functions and not derived
    ///     classes. This class is the one command for a looped cell and for a plain
    ///     one.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    internal class LoopedBindableAction<T> : ILoopedBindableAction<T>
        where T : notnull
    {
        private const int NotSampled = 0;

        private const int NotExecutable = 1;

        private const int Executable = 2;

        private readonly Func<object?, T> convertParameter;

        private readonly StreamSink<T> firingsStreamSink;

        /// <summary>
        ///     The sample from the constructor. See
        ///     <see cref="SampleAtCloseAndListenToUpdates{T}" /> for the cause.
        ///     <see cref="ReadCanExecute" /> reads it one time only.
        /// </summary>
        private readonly InitialSample<bool> initialCanExecute;

        /// <summary>
        ///     This field is necessary. The subscription to the availability cell is weak,
        ///     thus this field keeps it alive.
        /// </summary>
        private readonly IListener listener;

        private readonly IBindingScheduler scheduler;

        private readonly Action<object?> validateParameter;

        /// <summary>
        ///     <see cref="NotSampled" />, <see cref="NotExecutable" />, or
        ///     <see cref="Executable" />. Read it with <see cref="ReadCanExecute" />.
        /// </summary>
        private int canExecute;

        private int disposed;

        /// <param name="firingsStreamSink">The sink that each accepted call sends to.</param>
        /// <param name="isEnabledCell">The cell that controls availability.</param>
        /// <param name="scheduler">Moves notifications to the binding thread.</param>
        /// <param name="validateParameter">
        ///     Throws when the <c>CommandParameter</c> is not acceptable. This runs
        ///     before the send goes into the queue, thus the exception reaches the caller
        ///     of <see cref="Execute" />.
        /// </param>
        /// <param name="convertParameter">
        ///     Gives the value to send for a <c>CommandParameter</c> that
        ///     <paramref name="validateParameter" /> accepted.
        /// </param>
        internal LoopedBindableAction(
            StreamSink<T> firingsStreamSink,
            LoopedCell<bool> isEnabledCell,
            IBindingScheduler scheduler,
            Action<object?> validateParameter,
            Func<object?, T> convertParameter)
        {
            this.firingsStreamSink =
                firingsStreamSink
                ?? throw new ArgumentNullException(nameof(firingsStreamSink));

            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            this.validateParameter = validateParameter ?? throw new ArgumentNullException(nameof(validateParameter));
            this.convertParameter = convertParameter ?? throw new ArgumentNullException(nameof(convertParameter));
            this.IsEnabledCell = isEnabledCell ?? throw new ArgumentNullException(nameof(isEnabledCell));

            (this.initialCanExecute, this.listener) =
                SampleAtCloseAndListenToUpdates(cell: isEnabledCell, handler: this.OnIsEnabledChanged);
        }

        public event EventHandler? CanExecuteChanged;

        /// <inheritdoc />
        public Stream<T> FiringsStream => this.firingsStreamSink;

        /// <inheritdoc />
        public LoopedCell<bool> IsEnabledCell { get; }

        public bool CanExecute(object? parameter) => Volatile.Read(ref this.disposed) == 0 && this.ReadCanExecute();

        public void Execute(object? parameter)
        {
            if (!this.CanExecute(parameter))
            {
                return;
            }

            // This code tests the value here and not in the posted action. The test of the type
            // is a diagnostic for the author of the XAML, and it must throw where that person can
            // see it. When a transaction is open, PostWrite defers, and a throw from the deferred
            // action leaves Transaction.Close. That stops a transaction that has no connection to
            // this command, and discards the other work in its queue.
            this.validateParameter(parameter);

            PostWrite(() =>
            {
                // CanExecute reads a copy of the availability that the scheduler moves to the
                // binding thread, and that copy can be older than the cell. Thus, the send reads the
                // cell again in its own transaction, as Gate does, and a disabled command sends
                // nothing.
                if (!this.IsEnabledCell.SampleImpl())
                {
                    return;
                }

                this.firingsStreamSink.SendImpl(this.convertParameter(parameter));
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(location1: ref this.disposed, value: 1) != 0)
            {
                return;
            }

            this.listener.Unlisten();

            // This reads the sample first, thus the exchange gives a value and not NotSampled.
            // A binding engine can attach to CanExecuteChanged before it calls CanExecute, and
            // that engine must also get the notification.
            _ = this.ReadCanExecute();

            bool wasExecutable =
                Interlocked.Exchange(location1: ref this.canExecute, value: NotExecutable) == Executable;

            // This code removes the handlers before it raises the notification. Thus, a handler
            // cannot attach again and cannot run two times. The local variable keeps the one
            // notification that this command must still send.
            EventHandler? handler = this.CanExecuteChanged;
            this.CanExecuteChanged = null;

            if (!wasExecutable || handler == null)
            {
                return;
            }

            // A binding engine caches the last result of CanExecute and asks again only when
            // this command tells it to. Without this notification, a command that a caller
            // disposed looks available. A user can click it, and the click does nothing. This
            // code posts the notification and does not raise it on the calling thread, because a
            // caller can call Dispose from any thread.
            this.scheduler.Post(() => handler(sender: this, e: EventArgs.Empty));
        }

        /// <summary>
        ///     The rule for a command whose parameter is a <typeparamref name="T" />:
        ///     throws for a <c>CommandParameter</c> of a different type.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     <paramref name="value" /> is not a <typeparamref name="T" />, and it is not
        ///     a null that <typeparamref name="T" /> can hold.
        /// </exception>
        internal static void ValidateTyped(object? value)
        {
            if (value is T)
            {
                return;
            }

            throw GetInvalidTypeException();
        }

        /// <summary>
        ///     The conversion for a command whose parameter is a
        ///     <typeparamref name="T" />. <see cref="ValidateTyped" /> accepted the value
        ///     before this point, thus the conversion cannot fail.
        /// </summary>
        internal static T ConvertTyped(object? value) =>
            value switch
            {
                T typedValue => typedValue,
                _ => throw GetInvalidTypeException()
            };

        private static InvalidOperationException GetInvalidTypeException() =>
            new("The command parameter must be of type " + typeof(T).FullName + ".");

        // The same rule as LoopedOneWayBindableValue.OnSourceChanged. A transaction that fails
        // after this callback can discard the update of the cell. Thus, the posted action uses the
        // value here only when the outcome is known as committed, and reads the cell in each other
        // state.
        private void OnIsEnabledChanged(bool value)
        {
            TransactionOutcome outcome = TransactionInternal.CurrentOutcome();

            this.scheduler.Post(() =>
            {
                if (Volatile.Read(ref this.disposed) != 0)
                {
                    return;
                }

                bool authoritative = outcome.Committed ? value : this.IsEnabledCell.SampleImpl();

                if (this.ReadCanExecute() == authoritative)
                {
                    return;
                }

                Volatile.Write(location: ref this.canExecute, value: authoritative ? Executable : NotExecutable);
                this.CanExecuteChanged?.Invoke(sender: this, e: EventArgs.Empty);
            });
        }

        /// <summary>
        ///     Gives the availability, and puts the sample from the constructor into the
        ///     field on the first read.
        /// </summary>
        private bool ReadCanExecute()
        {
            int state = Volatile.Read(ref this.canExecute);

            if (state == NotSampled)
            {
                int sampled = this.initialCanExecute.Read() ? Executable : NotExecutable;

                // Dispose can write the field between the read above and this line. Its value
                // is newer than the sample, thus it wins.
                int previous =
                    Interlocked.CompareExchange(location1: ref this.canExecute, value: sampled, comparand: NotSampled);

                state = previous == NotSampled ? sampled : previous;
            }

            return state == Executable;
        }
    }

    /// <summary>
    ///     A <see cref="LoopedBindableAction{T}" /> for a <see cref="Cell{T}" /> of
    ///     <see cref="bool" />, which gives the cell back as one.
    /// </summary>
    /// <remarks>
    ///     This is a different class, and not a flag. Thus, a command built from a
    ///     looped cell is not an <see cref="IBindableAction{T}" />, also through a cast.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    internal class BindableAction<T> : LoopedBindableAction<T>, IBindableAction<T>
        where T : notnull
    {
        internal BindableAction(
            StreamSink<T> firingsStreamSink,
            Cell<bool> isEnabledCell,
            IBindingScheduler scheduler,
            Action<object?> validateParameter,
            Func<object?, T> convertParameter)
            : base(
                firingsStreamSink: firingsStreamSink,
                isEnabledCell: isEnabledCell,
                scheduler: scheduler,
                validateParameter: validateParameter,
                convertParameter: convertParameter) =>
            this.IsEnabledCell = isEnabledCell;

        /// <inheritdoc />
        public new Cell<bool> IsEnabledCell { get; }
    }
}
