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
            if (sender is IReadableBindableValue<T> notified &&
                args.PropertyName == nameof(IReadableBindableValue<>.Value))
            {
                action(notified.Value);
            }
        }
    }

    // ReSharper disable once ConvertToPrimaryConstructor - a primary constructor here cannot satisfy
    // this solution's settings: capturing its parameter in Dispose is disallowed, and holding it in a
    // field instead is then reported as a field that should be that parameter.
    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action onDispose;

        public ActionDisposable(Action onDispose) => this.onDispose = onDispose;

        /// <inheritdoc />
        public void Dispose() => this.onDispose();
    }
}
