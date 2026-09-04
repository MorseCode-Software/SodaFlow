using System;

namespace SodaFlow;

internal static class LazyExtensionMethodsInternal
{
    internal static Lazy<TResult> MapImpl<T, TResult>(this Lazy<T> a, Func<T, TResult> f) => new(() => f(a.Value));
}
