using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Makes a <see cref="LazySample{T}" /> for a value that is not a sample of a
///     cell or a behavior.
/// </summary>
[PublicAPI]
public static class LazySample
{
    /// <summary>
    ///     Makes a lazy sample from a function. SodaFlow runs the function when it
    ///     reads the sample.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="f">
    ///     Gives the value. It can run more than one time, thus it must be pure, and
    ///     it must not send into the graph.
    /// </param>
    /// <returns>The lazy sample.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<T> Defer<T>(Func<T> f) => LazySampleInternal.DeferImpl(f);
}
