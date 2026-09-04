using System;
using System.ComponentModel;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel
{
    public static partial class BindableCoreExtensionMethods
    {
        /// <summary>
        ///     Common plumbing for the notifying implementations: a single <c>"Value"</c>
        ///     property-changed notification, scheduler access, and idempotent disposal.
        /// </summary>
        private abstract class BindableValueBase : INotifyPropertyChanged, IDisposable
        {
            /// <summary>Cached to avoid allocating on every notification.</summary>
            private static readonly PropertyChangedEventArgs ValueChangedEventArgs = new("Value");

            private int disposed;

            protected BindableValueBase(IBindingScheduler? scheduler) =>
                this.Scheduler = BindingScheduler.Resolve(scheduler);

            public event PropertyChangedEventHandler? PropertyChanged;

            /// <summary>The scheduler used to marshal notifications onto the binding thread.</summary>
            protected IBindingScheduler Scheduler { get; }

            /// <summary>True once <see cref="Dispose" /> has run.</summary>
            protected bool IsDisposed => Volatile.Read(ref this.disposed) != 0;

            /// <summary>Raises <see cref="PropertyChanged" /> for <c>Value</c>. Call on the binding thread.</summary>
            protected void RaiseValueChanged() => this.PropertyChanged?.Invoke(sender: this, e: ValueChangedEventArgs);

            protected void ThrowIfDisposed()
            {
                if (this.IsDisposed)
                {
                    throw new ObjectDisposedException(this.GetType().FullName);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(location1: ref this.disposed, value: 1) != 0)
                {
                    return;
                }

                this.DisposeCore();
                this.PropertyChanged = null;
            }

            /// <summary>Unsubscribes from the FRP graph. Called at most once.</summary>
            protected abstract void DisposeCore();
        }
    }
}
