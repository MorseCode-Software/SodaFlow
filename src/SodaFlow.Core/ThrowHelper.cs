using System;

namespace SodaFlow;

// The runtime has ArgumentNullException.ThrowIfNull from net6.0, and ObjectDisposedException.ThrowIf from
// net7.0. The analyzers of the net8.0 build tell the code to use them, and not an explicit throw. The net472 and
// netstandard2.0 builds do not have them. Thus, each helper here calls the one of the runtime where it exists,
// and throws the same exception itself where it does not. The assemblies that see the internals of
// SodaFlow.Core use these helpers also.
internal static class ThrowHelper
{
    internal static void ThrowIfNull(object? argument, string paramName)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(argument: argument, paramName: paramName);
#else
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
#endif
    }

    internal static void ThrowIfDisposed(bool condition, object instance)
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(condition: condition, instance: instance);
#else
        if (condition)
        {
            throw new ObjectDisposedException(instance.GetType().FullName);
        }
#endif
    }
}
