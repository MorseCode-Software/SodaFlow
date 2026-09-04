using System;
using System.Runtime.CompilerServices;

namespace SodaFlow
{
    /// <summary>
    ///     The operations available on a <see cref="StreamSink{T}" />.
    /// </summary>
    /// <remarks>
    ///     A stream sink is how an event from outside the FRP graph is pushed into it. These operations
    ///     are for interfacing I/O to FRP only, and throw if called from inside a listener handler.
    /// </remarks>
    public static class StreamSinkExtensionMethods
    {
        /// <summary>
        ///     Send a value.  This method may not be called from inside handlers registered with
        ///     <see cref="StreamExtensionMethods.Listen{T}(Stream{T}, Action{T})" />,
        ///     <see cref="StreamExtensionMethods.ListenStrong{T}(Stream{T}, Action{T})" /> or either of their cell
        ///     equivalents.
        ///     An exception will be thrown, because sinks are for interfacing I/O to FRP only.  They are not meant to be used to
        ///     define new primitives.
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
}
