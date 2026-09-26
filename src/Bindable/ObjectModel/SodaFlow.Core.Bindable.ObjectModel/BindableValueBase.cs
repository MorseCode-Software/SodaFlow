using System;
using System.ComponentModel;
using System.Threading;

namespace SodaFlow.Bindable.ObjectModel;

public static partial class BindableCoreExtensionMethods
{
    /// <summary>
    ///     The shared parts of the implementations that give a notification. It supplies one
    ///     <c>"Value"</c> property-changed notification, access to the scheduler, and a Dispose
    ///     method that a caller can call more than one time.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private abstract class BindableValueBase : INotifyPropertyChanged, IDisposable
    {
        /// <summary>This is cached, thus each notification makes no allocation.</summary>
        private static readonly PropertyChangedEventArgs ValueChangedEventArgs = new("Value");

        private int disposed;

        protected BindableValueBase(IBindingScheduler scheduler) =>
            this.Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        /// <summary>The scheduler that moves a notification to the binding thread.</summary>
        protected IBindingScheduler Scheduler { get; }

        /// <summary>This is true after <see cref="Dispose" /> runs.</summary>
        protected bool IsDisposed => Volatile.Read(ref this.disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(location1: ref this.disposed, value: 1) != 0)
            {
                return;
            }

            this.DisposeCore();
            this.PropertyChanged = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raises <see cref="PropertyChanged" /> for <c>Value</c>. Call this on the binding thread.</summary>
        protected void RaiseValueChanged() => this.PropertyChanged?.Invoke(sender: this, e: ValueChangedEventArgs);

        protected void ThrowIfDisposed()
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(this.GetType().FullName);
            }
        }

        /// <summary>Stops the subscription to the FRP graph. SodaFlow calls this one time.</summary>
        protected abstract void DisposeCore();
    }
}
