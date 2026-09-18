using System;
using System.ComponentModel;

namespace SodaFlow.Bindable.ObjectModel.Tests;

internal static class TestUtility
{
    internal static IDisposable ListenForValueChanges<T>(this IReadableBindableValue<T> bindableValue, Action<T> action)
    {
        bindableValue.PropertyChanged += Handler;

        return new ActionDisposable(() => bindableValue.PropertyChanged -= Handler);

        void Handler(object? sender, PropertyChangedEventArgs args)
        {
            if (sender is IReadableBindableValue<T> notified
                && args.PropertyName == nameof(IReadableBindableValue<>.Value))
            {
                action(notified.Value);
            }
        }
    }

    // ReSharper disable once ConvertToPrimaryConstructor - a primary constructor here cannot satisfy
    // the settings of this solution. They do not permit the capture of its parameter in Dispose,
    // and a field that holds the parameter instead gets a report that it must be that
    // parameter.
    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action onDispose;

        public ActionDisposable(Action onDispose) => this.onDispose = onDispose;

        /// <inheritdoc />
        public void Dispose() => this.onDispose();
    }
}
