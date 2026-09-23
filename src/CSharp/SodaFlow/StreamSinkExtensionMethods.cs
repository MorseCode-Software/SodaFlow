using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="StreamSink{T}" />.
/// </summary>
/// <remarks>
///     A stream sink puts an event from other code into the FRP graph. These operations
///     are for interfacing I/O to FRP only, and throw if called in a listener handler.
/// </remarks>
[PublicAPI]
public static class StreamSinkExtensionMethods
{
    /// <summary>
    ///     Send a value. A caller must not call this method in a handler registered with
    ///     <see cref="StreamExtensionMethods.Listen{T}(Stream{T}, Action{T})" />,
    ///     <see cref="StreamExtensionMethods.ListenStrong{T}(Stream{T}, Action{T})" /> or one of their cell
    ///     equivalents. An exception will be thrown, because sinks are for interfacing I/O to FRP only. They
    ///     are not for the definition of a new primitive.
    /// </summary>
    /// <typeparam name="T">The type of the stream sink.</typeparam>
    /// <param name="s">The stream sink.</param>
    /// <param name="a">The value to send.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Send<T>(this StreamSink<T> s, T a) => s.SendImpl(a);

    /// <summary>
    ///     Return a reference to this <see cref="StreamSink{T}" /> as a <see cref="Stream{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the stream sink.</typeparam>
    /// <param name="s">The stream sink.</param>
    /// <returns>A reference to this <see cref="StreamSink{T}" /> as a <see cref="Stream{T}" />.</returns>
    public static Stream<T> AsStream<T>(this StreamSink<T> s) => s;
}
