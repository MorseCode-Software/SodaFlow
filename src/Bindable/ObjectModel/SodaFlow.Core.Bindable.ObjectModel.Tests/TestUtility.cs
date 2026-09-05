using System;
using System.ComponentModel;

namespace SodaFlow.Bindable.ObjectModel.Tests;

internal static class TestUtility
{
    internal static IDisposable ListenForValueChanges<T>(this IReadableBindableValue<T> bindableValue, Action<T> action)
    {
        bindableValue.PropertyChanged += Handler;

        return new ActionDisposable(() => bindableValue.PropertyChanged -= Handler);

        void Handler(object sender, PropertyChangedEventArgs args)
        {
            if (sender is IReadableBindableValue<T> notified &&
                args.PropertyName == nameof(IReadableBindableValue<T>.Value))
            {
                action(notified.Value);
            }
        }
    }

    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action onDispose;

        public ActionDisposable(Action onDispose) => this.onDispose = onDispose;

        /// <inheritdoc />
        public void Dispose() => this.onDispose();
    }
}
