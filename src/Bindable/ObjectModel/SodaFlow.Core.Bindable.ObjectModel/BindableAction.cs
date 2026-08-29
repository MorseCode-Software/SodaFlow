using System;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel
{
    public static partial class BindableCoreExtensionMethods
    {
        /// <summary>
        ///     A command that carries the <c>CommandParameter</c> into the stream.
        /// </summary>
        internal class BindableAction<T> : IBindableAction<T>
        {
            private readonly StreamSink<T> firingsStreamSink;

            /// <summary>
            ///     Load-bearing. The enablement subscription is weak, so this field is what keeps it
            ///     alive.
            /// </summary>
            private readonly IListener listener;

            private readonly IBindingScheduler scheduler;

            private bool canExecute;
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

                PostWrite(() => this.SendValue(streamSink: this.firingsStreamSink, value: parameter));
            }

            /// <summary>
            ///     Guards against a XAML author binding a <c>CommandParameter</c> to the wrong type.
            ///     Throws an <see cref="InvalidOperationException" /> if the type is not correct.
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