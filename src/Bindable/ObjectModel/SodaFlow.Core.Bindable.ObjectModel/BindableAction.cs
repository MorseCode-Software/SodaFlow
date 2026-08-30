using System;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel
{
    public static partial class BindableCoreExtensionMethods
    {
        /// <summary>
        ///     A command that carries the <c>CommandParameter</c> into the stream.
        /// </summary>
        /// <remarks>
        ///     Safe to construct on any thread. Enablement is sampled by whichever thread builds the
        ///     instance and read by the binding thread, which is why the field holding it is volatile;
        ///     every later change is marshalled through the scheduler.
        /// </remarks>
        internal class BindableAction<T> : IBindableAction<T>
        {
            private readonly StreamSink<T> firingsStreamSink;

            /// <summary>
            ///     Load-bearing. The enablement subscription is weak, so this field is what keeps it
            ///     alive.
            /// </summary>
            private readonly IListener listener;

            private readonly IBindingScheduler scheduler;

            /// <summary>
            ///     Volatile because the constructor samples it on whichever thread built the command
            ///     while the binding engine reads it on its own. A bool needs no box to do this.
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

                // Validated here rather than inside the posted action. The type check is a
                // diagnostic for the XAML author and has to be thrown where they can see it: with a
                // transaction already in flight, PostWrite defers, and a throw from the deferred
                // action escapes Transaction.Close instead - aborting a transaction that has
                // nothing to do with this command and discarding whatever else it had queued.
                this.ValidateParameter(parameter);

                PostWrite(() => this.SendValue(streamSink: this.firingsStreamSink, value: parameter));
            }

            /// <summary>
            ///     Guards against a XAML author binding a <c>CommandParameter</c> to the wrong type.
            ///     Runs before the send is queued, so the exception reaches the caller of
            ///     <see cref="Execute" />.
            /// </summary>
            /// <param name="value">The command parameter, as the binding engine supplied it.</param>
            /// <exception cref="InvalidOperationException">
            ///     <paramref name="value" /> is neither a <typeparamref name="T" /> nor a null that
            ///     <typeparamref name="T" /> can represent.
            /// </exception>
            protected virtual void ValidateParameter(object? value)
            {
                if (value is T || (value is null && default(T) == null))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "The command parameter must be of type " + typeof(T).FullName);
            }

            /// <summary>
            ///     Sends the parameter into the stream. <see cref="ValidateParameter" /> has already
            ///     accepted it, so the conversion here cannot fail.
            /// </summary>
            protected virtual void SendValue(StreamSink<T> streamSink, object? value) =>
                streamSink.SendImpl(
                    value switch
                    {
                        T typedValue => typedValue,
                        null when default(T) == null => default!,
                        _ => throw new InvalidOperationException(
                            "The command parameter must be of type " + typeof(T).FullName)
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

            public void Dispose()
            {
                if (Interlocked.Exchange(location1: ref this.disposed, value: 1) != 0)
                {
                    return;
                }

                this.listener.Unlisten();

                bool wasExecutable = this.canExecute;
                this.canExecute = false;

                // Detached before the notification is raised, so a handler cannot resubscribe or be
                // called twice; the local keeps the one send we still owe it.
                EventHandler? handler = this.CanExecuteChanged;
                this.CanExecuteChanged = null;

                if (!wasExecutable || handler == null)
                {
                    return;
                }

                // A binding engine caches the last CanExecute result and only asks again when told
                // to. Clearing the handlers without ever raising this left a disposed command
                // looking enabled: still clickable, and silently doing nothing when clicked.
                // Posted rather than raised inline because Dispose can be called from any thread.
                this.scheduler.Post(() => handler(this, EventArgs.Empty));
            }
        }
    }
}