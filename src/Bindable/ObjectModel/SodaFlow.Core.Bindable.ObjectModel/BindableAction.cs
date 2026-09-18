using System;
using System.Threading;
using System.Windows.Input;
using JetBrains.Annotations;

namespace SodaFlow.Bindable.ObjectModel;

[PublicAPI]
public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     An <see cref="ICommand" /> that moves its <c>CommandParameter</c> to the stream. A
    ///     <see cref="Cell{T}" /> of <see cref="bool" /> controls when the command is
    ///     available.
    /// </summary>
    /// <remarks>
    ///     You can build this on any thread. The thread that builds the instance samples the
    ///     availability, and the binding thread reads it. For that cause the field that holds it
    ///     is volatile. The scheduler moves each subsequent change to the binding thread.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    internal class BindableAction<T> : IBindableAction<T>
        where T : notnull
    {
        private readonly StreamSink<T> firingsStreamSink;

        /// <summary>
        ///     This field is necessary. The subscription to the availability cell is weak, thus
        ///     this field keeps it alive.
        /// </summary>
        private readonly IListener listener;

        private readonly IBindingScheduler scheduler;

        /// <summary>
        ///     This field is volatile, because the constructor samples it on the thread that
        ///     built the command and the binding engine reads it on a different thread. A bool
        ///     needs no box for this.
        /// </summary>
        private volatile bool canExecute;

        private int disposed;

        internal BindableAction(
            StreamSink<T> firingsStreamSink,
            Cell<bool>? isEnabledCell,
            IBindingScheduler? scheduler)
        {
            this.firingsStreamSink =
                firingsStreamSink
                ?? throw new ArgumentNullException(nameof(firingsStreamSink));

            this.scheduler = BindingScheduler.Resolve(scheduler);

            Cell<bool> resolvedIsEnabledCell = isEnabledCell ?? CellInternal.ConstantImpl(true);

            this.listener =
                TransactionInternal.RunImpl(() =>
                {
                    this.canExecute = resolvedIsEnabledCell.SampleImpl();
                    return ListenToUpdates(cell: resolvedIsEnabledCell, handler: this.OnIsEnabledChanged);
                });

            this.IsEnabledCell = resolvedIsEnabledCell;
        }

        public event EventHandler? CanExecuteChanged;

        /// <inheritdoc />
        public Stream<T> FiringsStream => this.firingsStreamSink;

        /// <inheritdoc />
        public Cell<bool> IsEnabledCell { get; }

        public bool CanExecute(object? parameter) => this.canExecute && Volatile.Read(ref this.disposed) == 0;

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
            this.ValidateParameter(parameter);

            PostWrite(() => this.SendValue(streamSink: this.firingsStreamSink, value: parameter));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(location1: ref this.disposed, value: 1) != 0)
            {
                return;
            }

            this.listener.Unlisten();

            bool wasExecutable = this.canExecute;
            this.canExecute = false;

            // This code removes the handlers before it raises the notification. Thus a handler
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

        private static InvalidOperationException GetInvalidTypeException() =>
            new("The command parameter must be of type " + typeof(T).FullName + ".");

        /// <summary>
        ///     Prevents a binding from the author of the XAML that gives a
        ///     <c>CommandParameter</c> of an incorrect type. This runs before the send goes into
        ///     the queue, thus the exception reaches the caller of <see cref="Execute" />.
        /// </summary>
        /// <param name="value">The command parameter, as the binding engine supplied it.</param>
        /// <exception cref="InvalidOperationException">
        ///     <paramref name="value" /> is not a <typeparamref name="T" />, and it is not a null
        ///     that <typeparamref name="T" /> can hold.
        /// </exception>
        protected virtual void ValidateParameter(object? value)
        {
            if (value is T)
            {
                return;
            }

            throw GetInvalidTypeException();
        }

        /// <summary>
        ///     Sends the parameter into the stream. <see cref="ValidateParameter" /> accepted it
        ///     before this point, thus the conversion here cannot fail.
        /// </summary>
        protected virtual void SendValue(StreamSink<T> streamSink, object? value) =>
            streamSink.SendImpl(
                value switch
                {
                    T typedValue => typedValue,
                    _ => throw GetInvalidTypeException()
                });

        private void OnIsEnabledChanged(bool value) =>
            this.scheduler.Post(() =>
            {
                if (Volatile.Read(ref this.disposed) != 0)
                {
                    return;
                }

                if (this.canExecute == value)
                {
                    return;
                }

                this.canExecute = value;
                this.CanExecuteChanged?.Invoke(sender: this, e: EventArgs.Empty);
            });
    }
}
